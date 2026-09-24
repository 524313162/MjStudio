using System.Text.Json;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Shared;

namespace MjStudio.Application.Services
{
    /// <summary>
    /// 设置服务：全局配置持久化到 %AppData%\MjStudio\settings.json
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MjStudio");
        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        private readonly MjStudioOptions _options;

        /// <summary>无参构造（CLI 入口用）：直接持有磁盘加载的配置</summary>
        public SettingsService()
        {
            _options = Load();
        }

        /// <summary>DI 构造：同步共享的 MjStudioOptions 单例（改完设置立即对 ProjectStorageManager 等生效）</summary>
        public SettingsService(MjStudioOptions options)
        {
            _options = options;
            var loaded = Load();
            options.ComfyUiBaseUrl = loaded.ComfyUiBaseUrl;
            options.ComfyUiLaunchPath = loaded.ComfyUiLaunchPath;
            options.ComfyUiOutputDir = loaded.ComfyUiOutputDir;
            options.ApiPort = loaded.ApiPort;
            options.ApiHost = loaded.ApiHost;
            options.DbRoot = loaded.DbRoot;
            options.ResourcesRoot = loaded.ResourcesRoot;
            options.WorkflowDir = loaded.WorkflowDir;
        }

        public MjStudioOptions Get() => _options;

        public void Update(MjStudioOptions options)
        {
            Save(options);
        }

        private static MjStudioOptions Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var opts = JsonSerializer.Deserialize<MjStudioOptions>(json);
                    if (opts is not null) return opts;
                }
            }
            catch { /* 忽略损坏配置，回退默认 */ }
            return new MjStudioOptions();
        }

        private static void Save(MjStudioOptions options)
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
    }
}
