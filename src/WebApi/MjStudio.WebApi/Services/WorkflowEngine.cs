using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace MjStudio.WebApi.Services
{
    /// <summary>
    /// 工作流任务（内存存储）
    /// </summary>
    public class WorkflowTask
    {
        public string TaskId { get; set; } = Guid.NewGuid().ToString("N");
        public string Workflow { get; set; } = "";
        public string Status { get; set; } = "queued";   // queued/running/completed/failed
        public string? Message { get; set; }
        public string? PromptId { get; set; }
        public string? MediaType { get; set; }
        public long? ResourceId { get; set; }
        public string? RelativePath { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 工作流运行请求（通用参数化）
    /// </summary>
    public class WorkflowRunRequest
    {
        /// <summary>工作流模板名（对应 workflows 目录下文件名，不含 .json）</summary>
        public string Workflow { get; set; } = "";

        /// <summary>节点输入注入：nodeId -> (inputKey -> value)</summary>
        public Dictionary<string, Dictionary<string, object?>>? NodeInputs { get; set; }

        /// <summary>图片注入：nodeId -> 图片绝对路径（LoadImage 节点）</summary>
        public Dictionary<string, string>? Images { get; set; }

        /// <summary>输出节点ID（SaveImage/SaveVideo/SaveAudio，用于定位产物）</summary>
        public string? OutputNode { get; set; }

        /// <summary>产物媒体类型（image/png、video/mp4、audio/wav 等）</summary>
        public string? MediaType { get; set; }

        /// <summary>产物落盘子目录（images/audio/video/voice/final）</summary>
        public string SubDir { get; set; } = "images";

        /// <summary>产物文件名（含扩展名）</summary>
        public string? FileName { get; set; }

        /// <summary>资源用途</summary>
        public ResourcePurposeEnum Purpose { get; set; } = ResourcePurposeEnum.Other;

        /// <summary>轮询超时（秒）</summary>
        public int TimeoutSec { get; set; } = 1800;
    }

    /// <summary>
    /// 工作流引擎：读取程序目录下工作流模板 → 注入参数 → 提交 ComfyUI → 轮询 → 取回源件 → 落盘项目 resources。
    /// 所有 ComfyUI 调用统一走本引擎（不再使用 Python 脚本）。
    /// </summary>
    public class WorkflowEngine
    {
        private readonly ComfyUiClient _comfy;
        private readonly ISettingsService _settings;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly CurrentProject _current;
        private readonly ConcurrentDictionary<string, WorkflowTask> _tasks = new();

        public WorkflowEngine(ComfyUiClient comfy, ISettingsService settings, IServiceScopeFactory scopeFactory, CurrentProject current)
        {
            _comfy = comfy;
            _settings = settings;
            _scopeFactory = scopeFactory;
            _current = current;
        }

        /// <summary>工作流模板目录（程序目录下 workflows）</summary>
        public string WorkflowDir
        {
            get
            {
                var dir = _settings.Get().WorkflowDir;
                return Path.IsPathRooted(dir) ? dir : Path.Combine(AppContext.BaseDirectory, dir);
            }
        }

        /// <summary>列出可用工作流模板名</summary>
        public List<string> ListWorkflows()
        {
            if (!Directory.Exists(WorkflowDir)) return new List<string>();
            return Directory.GetFiles(WorkflowDir, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .OrderBy(n => n)
                .ToList();
        }

        /// <summary>读取工作流模板原始 JSON 文本</summary>
        public string ReadWorkflow(string name)
        {
            var path = Path.Combine(WorkflowDir, name + ".json");
            if (!File.Exists(path))
                throw new FileNotFoundException($"工作流模板不存在: {path}");
            return File.ReadAllText(path);
        }

        /// <summary>保存工作流模板（新建或覆盖），返回规范化后的 JSON 文本</summary>
        public string SaveWorkflow(string name, string json)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("工作流名称不能为空");
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("工作流内容不能为空");

            // 校验 JSON 合法性并规范化缩进
            var node = JsonNode.Parse(json)
                ?? throw new InvalidOperationException("工作流内容不是有效的 JSON");
            if (node is not JsonObject)
                throw new InvalidOperationException("工作流内容必须是 JSON 对象");

            Directory.CreateDirectory(WorkflowDir);
            var path = Path.Combine(WorkflowDir, name + ".json");
            var normalized = node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, normalized);
            return normalized;
        }

        /// <summary>删除工作流模板</summary>
        public void DeleteWorkflow(string name)
        {
            var path = Path.Combine(WorkflowDir, name + ".json");
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        /// 发起工作流（异步执行，立即返回 taskId）。
        /// </summary>
        public WorkflowTask Submit(WorkflowRunRequest req)
        {
            var task = new WorkflowTask { Workflow = req.Workflow };
            _tasks[task.TaskId] = task;

            _ = Task.Run(async () =>
            {
                try
                {
                    task.Status = "running";
                    var workflow = LoadAndInject(req);
                    task.PromptId = await _comfy.SubmitAsync(workflow);
                    var outputs = await _comfy.PollAsync(task.PromptId, req.TimeoutSec);
                    var file = PickOutput(outputs, req.OutputNode);
                    if (file is null)
                        throw new InvalidOperationException("工作流未产出文件");

                    var bytes = _comfy.FetchOutput(file);
                    task.MediaType = req.MediaType ?? GuessMediaType(file.Kind);

                    // 落盘项目 resources（通过作用域解析 Scoped 服务）
                    using var scope = _scopeFactory.CreateScope();
                    var resources = scope.ServiceProvider.GetRequiredService<IResourceService>();
                    var project = await resources.GetProjectIdAsync();
                    var fileName = req.FileName ?? $"{task.TaskId}{Path.GetExtension(file.Filename)}";
                    var resource = await resources.SaveAsync(project, bytes, req.SubDir, fileName,
                        task.MediaType, req.Purpose);
                    task.ResourceId = resource.Id;
                    task.RelativePath = resource.RelativePath;
                    task.Status = "completed";
                    task.Message = "完成";
                }
                catch (Exception ex)
                {
                    task.Status = "failed";
                    task.Message = ex.Message;
                }
            });

            return task;
        }

        /// <summary>
        /// 直接提交完整工作流 JSON（不落盘、不注入），立即返回 ComfyUI 原始响应 JSON。
        /// 用于工作流页「发起请求」：每个工作流输入不同，直接发送整个 JSON 内容。
        /// </summary>
        public async Task<JsonNode?> SubmitRawAsync(string json, CancellationToken ct = default)
        {
            var workflow = JsonNode.Parse(json)
                ?? throw new InvalidOperationException("工作流内容不是有效的 JSON");
            if (workflow is not JsonObject)
                throw new InvalidOperationException("工作流内容必须是 JSON 对象");
            return await _comfy.SubmitRawAsync(workflow, ct);
        }

        public WorkflowTask? Get(string taskId)
            => _tasks.TryGetValue(taskId, out var t) ? t : null;

        /// <summary>
        /// 读取工作流模板并注入参数。
        /// </summary>
        private JsonNode LoadAndInject(WorkflowRunRequest req)
        {
            var path = Path.Combine(WorkflowDir, req.Workflow + ".json");
            if (!File.Exists(path))
                throw new FileNotFoundException($"工作流模板不存在: {path}");

            var workflow = JsonNode.Parse(File.ReadAllText(path));
            if (workflow is not JsonObject wf)
                throw new InvalidOperationException($"工作流格式错误: {req.Workflow}");

            // 注入节点输入
            if (req.NodeInputs is not null)
            {
                foreach (var (nodeId, inputs) in req.NodeInputs)
                {
                    if (wf[nodeId] is not JsonObject node)
                        throw new InvalidOperationException($"工作流缺少节点 {nodeId}");
                    if (node["inputs"] is not JsonObject nodeInputs)
                        node["inputs"] = new JsonObject();
                    var ni = (JsonObject)node["inputs"]!;
                    foreach (var (key, value) in inputs)
                        ni[key] = ToJson(value);
                }
            }

            // 注入图片（LoadImage 节点：image 字段 = 文件名，需先复制到 ComfyUI input）
            if (req.Images is not null)
            {
                foreach (var (nodeId, absPath) in req.Images)
                {
                    if (wf[nodeId] is not JsonObject node)
                        throw new InvalidOperationException($"工作流缺少图片节点 {nodeId}");
                    if (node["inputs"] is not JsonObject nodeInputs)
                        node["inputs"] = new JsonObject();
                    var ni = (JsonObject)node["inputs"]!;
                    ni["image"] = Path.GetFileName(absPath);
                    // 复制到 ComfyUI input 目录
                    CopyToComfyInput(absPath);
                }
            }

            return workflow;
        }

        private void CopyToComfyInput(string absPath)
        {
            var inputDir = Path.Combine(
                new Uri(_settings.Get().ComfyUiBaseUrl).LocalPath.TrimEnd('/'), "input");
            // 若 LocalPath 不可用则用配置的 output 同级 input
            if (!Directory.Exists(inputDir))
            {
                var outputDir = _settings.Get().ComfyUiOutputDir;
                if (!string.IsNullOrEmpty(outputDir))
                    inputDir = Path.Combine(Path.GetDirectoryName(outputDir) ?? "", "input");
            }
            if (Directory.Exists(inputDir))
                File.Copy(absPath, Path.Combine(inputDir, Path.GetFileName(absPath)), overwrite: true);
        }

        private static ComfyOutputFile? PickOutput(List<ComfyOutputFile> outputs, string? outputNode)
        {
            if (outputs.Count == 0) return null;
            if (!string.IsNullOrEmpty(outputNode))
            {
                var match = outputs.FirstOrDefault(o => o.NodeId == outputNode);
                if (match is not null) return match;
            }
            return outputs[0];
        }

        private static string GuessMediaType(string kind) => kind switch
        {
            "images" => "image/png",
            "audio" => "audio/wav",
            "video" => "video/mp4",
            _ => "application/octet-stream"
        };

        private static JsonNode? ToJson(object? value) => value switch
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
}
