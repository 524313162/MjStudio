using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Shared;
using MjStudio.Infrastructure;
using MjStudio.WebApi.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace MjStudio.WebApi.Services
{
    /// <summary>
    /// 生成任务（内存存储，9 种业务生成类型共用）
    /// </summary>
    public class GenerationTask
    {
        public string TaskId { get; set; } = Guid.NewGuid().ToString("N");
        public string Type { get; set; } = "";
        public string Status { get; set; } = "queued";   // queued / running / completed / failed
        public string? Message { get; set; }
        public string? PromptId { get; set; }
        public string? MediaType { get; set; }
        public long? ResourceId { get; set; }
        public string? RelativePath { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        internal TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// 生成引擎：封装 9 种业务生成接口（ComfyUI 工作流 / ffmpeg）。
    /// 流程：Submit（立即返回 taskId）→ PollAsync（阻塞等待最多 60s，超时返回 running）。
    /// 后台任务负责：加载工作流 → 注入参数 → 提交 ComfyUI → 轮询完成 → 取回源件 → 落盘 resources。
    /// </summary>
    public class GenerationEngine
    {
        private readonly ComfyUiClient _comfy;
        private readonly ISettingsService _settings;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly CurrentProject _current;
        private readonly FfmpegService _ffmpeg;
        private readonly ResourceStorageService _storage;
        private readonly ConcurrentDictionary<string, GenerationTask> _tasks = new();

        public GenerationEngine(
            ComfyUiClient comfy,
            ISettingsService settings,
            IServiceScopeFactory scopeFactory,
            CurrentProject current,
            FfmpegService ffmpeg,
            ResourceStorageService storage)
        {
            _comfy = comfy;
            _settings = settings;
            _scopeFactory = scopeFactory;
            _current = current;
            _ffmpeg = ffmpeg;
            _storage = storage;
        }

        // ==================== 公共 API ====================

        /// <summary>
        /// 提交生成任务（立即返回 taskId，后台异步执行）。
        /// </summary>
        public GenerationTask Submit(string type, object request)
        {
            var task = new GenerationTask { Type = type };
            _tasks[task.TaskId] = task;

            _ = Task.Run(async () =>
            {
                try
                {
                    task.Status = "running";
                    await ExecuteAsync(task, type, request);
                    task.Status = "completed";
                    task.Message = "完成";
                }
                catch (Exception ex)
                {
                    task.Status = "failed";
                    task.Message = ex.Message;
                }
                finally
                {
                    task.Completion.TrySetResult(true);
                }
            });

            return task;
        }

        /// <summary>
        /// 查询任务状态（阻塞等待最多 timeoutSec 秒，超时返回当前状态）。
        /// </summary>
        public async Task<GenerationTask> PollAsync(string taskId, int timeoutSec = 60)
        {
            if (!_tasks.TryGetValue(taskId, out var task))
                throw new KeyNotFoundException($"任务 {taskId} 不存在");

            try
            {
                await task.Completion.Task.WaitAsync(TimeSpan.FromSeconds(timeoutSec));
            }
            catch (TimeoutException)
            {
                // 仍在运行，返回当前状态
            }

            return task;
        }

        public GenerationTask? Get(string taskId)
            => _tasks.TryGetValue(taskId, out var t) ? t : null;

        // ==================== 执行分发 ====================

        private async Task ExecuteAsync(GenerationTask task, string type, object request)
        {
            switch (type)
            {
                case "character":
                    await ExecuteImageGenAsync(task, "01.文生图（ZImage）-角色视图", (ImageGenRequest)request);
                    break;
                case "scene":
                    await ExecuteImageGenAsync(task, "01.文生图（ZImage）-场景俯视图", (ImageGenRequest)request);
                    break;
                case "prop":
                    await ExecuteImageGenAsync(task, "01.文生图（ZImage）-道具双视图", (ImageGenRequest)request);
                    break;
                case "bgm":
                    await ExecuteAudioGenAsync(task, "09.STABLE-BGM", (AudioGenRequest)request, "Music");
                    break;
                case "sfx":
                    await ExecuteAudioGenAsync(task, "09.STABLE-BGM", (AudioGenRequest)request, "SFX");
                    break;
                case "imageEdit":
                    await ExecuteImageEditAsync(task, (ImageEditRequest)request);
                    break;
                case "video":
                    await ExecuteVideoGenAsync(task, (VideoGenRequest)request);
                    break;
                case "videoCompose":
                    await ExecuteVideoComposeAsync(task, (VideoComposeRequest)request);
                    break;
                case "videoExport":
                    await ExecuteVideoExportAsync(task, (VideoExportRequest)request);
                    break;
                default:
                    throw new InvalidOperationException($"未知生成类型: {type}");
            }
        }

        // ==================== 1-3. 图片生成（ZImage 文生图） ====================

        private async Task ExecuteImageGenAsync(GenerationTask task, string template, ImageGenRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Prompt))
                throw new InvalidOperationException("提示词不能为空");

            var workflow = LoadWorkflow(template);
            SetNodeInput(workflow, "67", "text", req.Prompt);
            if (!string.IsNullOrWhiteSpace(req.NegativePrompt))
                SetNodeInput(workflow, "71", "text", req.NegativePrompt);
            SetNodeInput(workflow, "68", "width", req.Width);
            SetNodeInput(workflow, "68", "height", req.Height);
            if (req.Seed is not null)
                SetNodeInput(workflow, "70", "seed", req.Seed.Value);

            var (bytes, mediaType) = await RunComfyAndFetchAsync(task, workflow, "images");
            await SaveToResourcesAsync(task, bytes, "images",
                req.FileName ?? $"gen_{task.Type}_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                mediaType, ResourcePurposeEnum.AssetImage);
        }

        // ==================== 4-5. 音频生成（STABLE-BGM） ====================

        private async Task ExecuteAudioGenAsync(GenerationTask task, string template, AudioGenRequest req, string category)
        {
            if (string.IsNullOrWhiteSpace(req.Prompt))
                throw new InvalidOperationException("描述不能为空");

            var workflow = LoadWorkflow(template);
            SetNodeInput(workflow, "52:31", "value", req.Prompt);
            SetNodeInput(workflow, "52:36", "value", req.Duration);
            SetNodeInput(workflow, "52:43", "choice", category);

            var (bytes, mediaType) = await RunComfyAndFetchAsync(task, workflow, "audio");
            await SaveToResourcesAsync(task, bytes, "audio",
                req.FileName ?? $"gen_{task.Type}_{DateTime.Now:yyyyMMdd_HHmmss}.mp3",
                mediaType, ResourcePurposeEnum.AssetAudio);
        }

        // ==================== 6. 图片编辑（FLUX2） ====================

        private async Task ExecuteImageEditAsync(GenerationTask task, ImageEditRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.SourceImage))
                throw new InvalidOperationException("源图片路径不能为空");
            if (string.IsNullOrWhiteSpace(req.Prompt))
                throw new InvalidOperationException("编辑指令不能为空");

            var srcAbs = ResolveResourcePath(req.SourceImage);
            if (!File.Exists(srcAbs))
                throw new FileNotFoundException($"源图片不存在: {srcAbs}");

            var workflow = LoadWorkflow("02.1.FLUX2-图片编辑");
            var srcFileName = Path.GetFileName(srcAbs);
            CopyToComfyInput(srcAbs);
            SetNodeInput(workflow, "76", "image", srcFileName);
            SetNodeInput(workflow, "92:113", "text", req.Prompt);
            if (!string.IsNullOrWhiteSpace(req.NegativePrompt))
                SetNodeInput(workflow, "92:87", "text", req.NegativePrompt);

            var (bytes, mediaType) = await RunComfyAndFetchAsync(task, workflow, "images");
            await SaveToResourcesAsync(task, bytes, "images",
                req.FileName ?? $"edit_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                mediaType, ResourcePurposeEnum.AssetImage);
        }

        // ==================== 7. 视频生成（MinimaxH3 多图参考） ====================

        private async Task ExecuteVideoGenAsync(GenerationTask task, VideoGenRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Prompt))
                throw new InvalidOperationException("视频提示词不能为空");
            if (req.Images.Count < 1)
                throw new InvalidOperationException("至少需要 1 张参考图");
            if (req.Images.Count > 20)
                throw new InvalidOperationException("最多支持 20 张参考图");

            var count = req.Images.Count;
            var template = $"15.MinimaxH3 多图参考{count:00}";
            var workflow = LoadWorkflow(template);

            // 注入提示词和时长
            SetNodeInput(workflow, "7", "prompt", req.Prompt);
            SetNodeInput(workflow, "7", "seconds", req.Seconds);

            // 注入参考图（LoadImage 节点 201~20N + MiniMaxH3Easy 的 media_N / media_type_N）
            for (int i = 0; i < count; i++)
            {
                var relPath = req.Images[i];
                var absPath = ResolveResourcePath(relPath);
                if (!File.Exists(absPath))
                    throw new FileNotFoundException($"参考图 {i + 1} 不存在: {absPath}");

                var fileName = Path.GetFileName(absPath);
                CopyToComfyInput(absPath);

                var nodeId = (201 + i).ToString();
                SetNodeInput(workflow, nodeId, "image", fileName);
                SetNodeInput(workflow, "7", $"media_{i + 1}", new JsonArray(nodeId, 0));
                SetNodeInput(workflow, "7", $"media_type_{i + 1}", "image");
            }

            var (bytes, mediaType) = await RunComfyAndFetchAsync(task, workflow, "gifs", "video");
            await SaveToResourcesAsync(task, bytes, "video",
                req.FileName ?? $"video_{DateTime.Now:yyyyMMdd_HHmmss}.mp4",
                mediaType, ResourcePurposeEnum.ShotVideo);
        }

        // ==================== 8. 视频合成（ffmpeg concat） ====================

        private async Task ExecuteVideoComposeAsync(GenerationTask task, VideoComposeRequest req)
        {
            if (req.Videos.Count < 2)
                throw new InvalidOperationException("至少需要 2 个视频才能拼接");

            var absPaths = req.Videos.Select(ResolveResourcePath).ToList();
            for (int i = 0; i < absPaths.Count; i++)
                if (!File.Exists(absPaths[i]))
                    throw new FileNotFoundException($"视频 {i + 1} 不存在: {absPaths[i]}");

            var tempDir = Path.Combine(Path.GetTempPath(), $"mjstudio_{task.TaskId}");
            Directory.CreateDirectory(tempDir);
            var outputPath = Path.Combine(tempDir, "composed.mp4");

            try
            {
                _ffmpeg.ConcatVideos(absPaths, outputPath);
                var bytes = File.ReadAllBytes(outputPath);
                await SaveToResourcesAsync(task, bytes, "video",
                    req.FileName ?? $"composed_{DateTime.Now:yyyyMMdd_HHmmss}.mp4",
                    "video/mp4", ResourcePurposeEnum.FinalVideo);
            }
            finally
            {
                TryDeleteDir(tempDir);
            }
        }

        // ==================== 9. 视频导出（声音转换 + 拆分 + 合成） ====================

        private async Task ExecuteVideoExportAsync(GenerationTask task, VideoExportRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.SourceVideo))
                throw new InvalidOperationException("源视频路径不能为空");
            if (string.IsNullOrWhiteSpace(req.TargetVoice))
                throw new InvalidOperationException("声线种子路径不能为空");

            var videoAbs = ResolveResourcePath(req.SourceVideo);
            if (!File.Exists(videoAbs))
                throw new FileNotFoundException($"源视频不存在: {videoAbs}");

            var voiceAbs = ResolveResourcePath(req.TargetVoice);
            if (!File.Exists(voiceAbs))
                throw new FileNotFoundException($"声线种子不存在: {voiceAbs}");

            var tempDir = Path.Combine(Path.GetTempPath(), $"mjstudio_{task.TaskId}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // ① 从源视频提取音频
                var extractedAudio = Path.Combine(tempDir, "extracted_audio.wav");
                _ffmpeg.ExtractAudio(videoAbs, extractedAudio);

                // ② 声音转换（ComfyUI 工作流 32）
                var voiceFileName = Path.GetFileName(voiceAbs);
                var audioFileName = Path.GetFileName(extractedAudio);
                CopyToComfyInput(voiceAbs);
                CopyToComfyInput(extractedAudio);

                var workflow = LoadWorkflow("32.FL-CosyVoice3-音频声音转换");
                SetNodeInput(workflow, "1", "audio", voiceFileName);
                SetNodeInput(workflow, "2", "audio", audioFileName);

                var (convertedBytes, _) = await RunComfyAndFetchAsync(task, workflow, "audio");
                var convertedAudio = Path.Combine(tempDir, "converted_audio.wav");
                File.WriteAllBytes(convertedAudio, convertedBytes);

                // ③ 提取无声视频
                var videoOnly = Path.Combine(tempDir, "video_only.mp4");
                _ffmpeg.ExtractVideoOnly(videoAbs, videoOnly);

                // ④ 合成新视频（无声视频 + 转换后音频）
                var finalOutput = Path.Combine(tempDir, "final.mp4");
                _ffmpeg.MergeVideoAudio(videoOnly, convertedAudio, finalOutput);

                var bytes = File.ReadAllBytes(finalOutput);
                await SaveToResourcesAsync(task, bytes, "video",
                    req.FileName ?? $"export_{DateTime.Now:yyyyMMdd_HHmmss}.mp4",
                    "video/mp4", ResourcePurposeEnum.FinalVideo);
            }
            finally
            {
                TryDeleteDir(tempDir);
            }
        }

        // ==================== ComfyUI 通用执行 ====================

        /// <summary>
        /// 提交工作流到 ComfyUI → 轮询完成 → 取回源件字节。
        /// </summary>
        private async Task<(byte[] Bytes, string MediaType)> RunComfyAndFetchAsync(
            GenerationTask task, JsonNode workflow, params string[] outputKinds)
        {
            task.PromptId = await _comfy.SubmitAsync(workflow);
            var outputs = await _comfy.PollAsync(task.PromptId, timeoutSec: 1800);

            var file = outputs.FirstOrDefault(o => outputKinds.Contains(o.Kind))
                ?? throw new InvalidOperationException($"工作流未产出预期文件（期望: {string.Join("/", outputKinds)}）");

            var bytes = _comfy.FetchOutput(file);
            var mediaType = GuessMediaType(file.Kind);
            return (bytes, mediaType);
        }

        // ==================== 辅助方法 ====================

        private JsonNode LoadWorkflow(string name)
        {
            var dir = _settings.Get().WorkflowDir;
            if (!Path.IsPathRooted(dir))
                dir = Path.Combine(AppContext.BaseDirectory, dir);

            var path = Path.Combine(dir, name + ".json");
            if (!File.Exists(path))
                throw new FileNotFoundException($"工作流模板不存在: {path}");

            var node = JsonNode.Parse(File.ReadAllText(path))
                ?? throw new InvalidOperationException($"工作流模板解析失败: {name}");
            if (node is not JsonObject)
                throw new InvalidOperationException($"工作流模板格式错误（应为 JSON 对象）: {name}");
            return node;
        }

        private static void SetNodeInput(JsonNode workflow, string nodeId, string key, object? value)
        {
            if (workflow[nodeId] is not JsonObject node)
                throw new InvalidOperationException($"工作流缺少节点 {nodeId}");
            if (node["inputs"] is not JsonObject inputs)
                node["inputs"] = new JsonObject();
            var ni = (JsonObject)node["inputs"]!;
            ni[key] = value switch
            {
                null => null,
                JsonNode n => n,
                string s => s,
                bool b => b,
                int i => i,
                long l => l,
                float f => f,
                double d => d,
                _ => value.ToString()
            };
        }

        private string GetComfyInputDir()
        {
            var inputDir = Path.Combine(
                new Uri(_settings.Get().ComfyUiBaseUrl).LocalPath.TrimEnd('/'), "input");
            if (!Directory.Exists(inputDir))
            {
                var outputDir = _settings.Get().ComfyUiOutputDir;
                if (!string.IsNullOrEmpty(outputDir))
                    inputDir = Path.Combine(Path.GetDirectoryName(outputDir) ?? "", "input");
            }
            return inputDir;
        }

        private void CopyToComfyInput(string absPath)
        {
            var inputDir = GetComfyInputDir();
            if (!Directory.Exists(inputDir))
                throw new InvalidOperationException($"ComfyUI input 目录不存在: {inputDir}");
            File.Copy(absPath, Path.Combine(inputDir, Path.GetFileName(absPath)), overwrite: true);
        }

        private string ResolveResourcePath(string relativePath)
        {
            var projectName = _current.Require();
            return _storage.GetAbsolutePath(projectName, relativePath);
        }

        private async Task SaveToResourcesAsync(
            GenerationTask task, byte[] bytes, string subDir, string fileName,
            string mediaType, ResourcePurposeEnum purpose)
        {
            using var scope = _scopeFactory.CreateScope();
            var resources = scope.ServiceProvider.GetRequiredService<IResourceService>();
            var projectId = await resources.GetProjectIdAsync();
            var resource = await resources.SaveAsync(projectId, bytes, subDir, fileName, mediaType, purpose);
            task.MediaType = mediaType;
            task.ResourceId = resource.Id;
            task.RelativePath = resource.RelativePath;
        }

        private static string GuessMediaType(string kind) => kind switch
        {
            "images" => "image/png",
            "audio" => "audio/mpeg",
            "gifs" or "video" => "video/mp4",
            _ => "application/octet-stream"
        };

        private static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { /* 忽略清理失败 */ }
        }
    }
}
