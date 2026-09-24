namespace MjStudio.Domain.Shared
{
    /// <summary>
    /// 应用全局配置（ComfyUI 地址、API 端口、项目根目录等）
    /// </summary>
    public class MjStudioOptions
    {
        /// <summary>ComfyUI 服务地址</summary>
        public string ComfyUiBaseUrl { get; set; } = "http://127.0.0.1:8188";

        /// <summary>ComfyUI 程序启动地址（可执行文件路径，用于未启动时自动拉起）</summary>
        public string ComfyUiLaunchPath { get; set; } = "";

        /// <summary>ComfyUI output 目录（取回源件用，避免 8188/view 水印）</summary>
        public string ComfyUiOutputDir { get; set; } = "";

        /// <summary>内置 API 监听端口（默认 6066，可在设置中修改后重启 API）</summary>
        public int ApiPort { get; set; } = 6066;

        /// <summary>内置 API 监听地址</summary>
        public string ApiHost { get; set; } = "127.0.0.1";

        /// <summary>数据库目录（每个项目一个 &lt;项目名&gt;.db 文件，项目全部数据都在该 DB 中）</summary>
        public string DbRoot { get; set; } = "D:\\00-ai-project\\MjStudio";

        /// <summary>
        /// 资源目录（所有项目的媒体文件统一存放处）：
        /// &lt;ResourcesRoot&gt;\&lt;项目名&gt;\{images,audio,video,voice,final}。
        /// </summary>
        public string ResourcesRoot { get; set; } = "D:\\00-ai-project\\MjStudio\\resources";

        /// <summary>工作流模板目录（程序目录下 workflows）</summary>
        public string WorkflowDir { get; set; } = "workflows";
    }
}
