using System.Text.Json;
using System.Text.Json.Nodes;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Shared;

namespace MjStudio.WebApi.Services
{
    /// <summary>
    /// ComfyUI 产物文件条目
    /// </summary>
    public class ComfyOutputFile
    {
        public string NodeId { get; set; } = "";
        public string Kind { get; set; } = "";   // images / audio / video
        public string Filename { get; set; } = "";
        public string Subfolder { get; set; } = "";
        public string Type { get; set; } = "output";
    }

    /// <summary>
    /// ComfyUI 客户端：提交工作流 → 轮询历史 → 从 output 目录取回源件。
    /// 取回铁律：从 output 目录源件直接读取（严禁 8188/view 下载通道，会带"AI生成"水印）。
    /// </summary>
    public class ComfyUiClient
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly ISettingsService _settings;

        public ComfyUiClient(IHttpClientFactory httpFactory, ISettingsService settings)
        {
            _httpFactory = httpFactory;
            _settings = settings;
        }

        private string BaseUrl => _settings.Get().ComfyUiBaseUrl.TrimEnd('/');

        /// <summary>
        /// 提交工作流，返回 prompt_id。
        /// </summary>
        public async Task<string> SubmitAsync(JsonNode workflow, CancellationToken ct = default)
        {
            var result = await SubmitRawAsync(workflow, ct);
            var promptId = result?["prompt_id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(promptId))
                throw new InvalidOperationException($"ComfyUI 提交未返回 prompt_id: {result?.ToJsonString()}");
            return promptId;
        }

        /// <summary>
        /// 提交工作流，返回 ComfyUI 原始响应 JSON（含 prompt_id、node_errors 等）。
        /// </summary>
        public async Task<JsonNode?> SubmitRawAsync(JsonNode workflow, CancellationToken ct = default)
        {
            var client = _httpFactory.CreateClient();
            var payload = new JsonObject
            {
                ["prompt"] = workflow,
                ["client_id"] = $"mjstudio-{Guid.NewGuid():N}"[..20]
            };
            using var resp = await client.PostAsJsonAsync($"{BaseUrl}/prompt", payload, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"ComfyUI 提交失败 ({(int)resp.StatusCode}): {body}");

            return JsonNode.Parse(body);
        }

        /// <summary>
        /// 轮询直到完成，返回产物文件列表。
        /// </summary>
        public async Task<List<ComfyOutputFile>> PollAsync(string promptId, int timeoutSec = 1800, int intervalSec = 5, CancellationToken ct = default)
        {
            var client = _httpFactory.CreateClient();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (sw.Elapsed.TotalSeconds < timeoutSec)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(intervalSec), ct);

                using var resp = await client.GetAsync($"{BaseUrl}/history/{promptId}", ct);
                if (!resp.IsSuccessStatusCode) continue;
                var body = await resp.Content.ReadAsStringAsync(ct);
                var json = JsonNode.Parse(body);
                var rec = json?[promptId];
                if (rec is null) continue;

                var statusStr = rec?["status"]?["status_str"]?.GetValue<string>();
                var completed = rec?["status"]?["completed"]?.GetValue<bool>() ?? false;

                if (statusStr == "success" && completed)
                    return ExtractOutputs(rec?["outputs"]);

                if (statusStr == "error")
                    throw new InvalidOperationException($"ComfyUI 任务执行失败: {rec?["status"]?.ToJsonString()}");
            }
            throw new TimeoutException($"ComfyUI 任务超时 (>{timeoutSec}s): {promptId}");
        }

        /// <summary>
        /// 从 output 目录取回源件字节（按产物条目）。
        /// </summary>
        public byte[] FetchOutput(ComfyOutputFile file)
        {
            var outputDir = _settings.Get().ComfyUiOutputDir;
            if (string.IsNullOrEmpty(outputDir))
                throw new InvalidOperationException("未配置 ComfyUI output 目录（settings.ComfyUiOutputDir）");

            var src = string.IsNullOrEmpty(file.Subfolder)
                ? Path.Combine(outputDir, file.Filename)
                : Path.Combine(outputDir, file.Subfolder, file.Filename);

            if (!File.Exists(src))
                throw new FileNotFoundException($"取回源件不存在: {src}");
            return File.ReadAllBytes(src);
        }

        private static List<ComfyOutputFile> ExtractOutputs(JsonNode? outputs)
        {
            var files = new List<ComfyOutputFile>();
            if (outputs is null) return files;

            using var doc = JsonDocument.Parse(outputs.ToJsonString());
            foreach (var nodeProp in doc.RootElement.EnumerateObject())
            {
                var nodeId = nodeProp.Name;
                if (nodeProp.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var kindProp in nodeProp.Value.EnumerateObject())
                {
                    var kind = kindProp.Name;
                    if (kind is not ("images" or "audio" or "gifs" or "video")) continue;
                    var entries = kindProp.Value;
                    if (entries.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ent in entries.EnumerateArray())
                        {
                            files.Add(new ComfyOutputFile
                            {
                                NodeId = nodeId,
                                Kind = kind,
                                Filename = ent.TryGetProperty("filename", out var f) ? f.GetString() ?? "" : "",
                                Subfolder = ent.TryGetProperty("subfolder", out var s) ? s.GetString() ?? "" : "",
                                Type = ent.TryGetProperty("type", out var t) ? t.GetString() ?? "output" : "output"
                            });
                        }
                    }
                    else if (entries.ValueKind == JsonValueKind.Object && kind == "video")
                    {
                        files.Add(new ComfyOutputFile
                        {
                            NodeId = nodeId,
                            Kind = "video",
                            Filename = entries.TryGetProperty("filename", out var f) ? f.GetString() ?? "" : "",
                            Subfolder = entries.TryGetProperty("subfolder", out var s) ? s.GetString() ?? "" : "",
                            Type = "output"
                        });
                    }
                }
            }
            return files;
        }
    }
}
