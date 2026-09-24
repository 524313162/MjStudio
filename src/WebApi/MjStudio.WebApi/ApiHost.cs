using MjStudio.Application.Services;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Shared;
using MjStudio.Infrastructure;
using MjStudio.WebApi.Services;

namespace MjStudio.WebApi
{
    /// <summary>
    /// 内置 API 宿主：构建 WebApplication（Kestrel），注册全部服务。
    /// WPF Host 通过 <see cref="Build"/> 内嵌启动；也可经 Program.cs 独立运行。
    /// 返回的 WebApplication.Services 即全局 DI 容器（UI 与 API 共享同一容器）。
    /// </summary>
    public static class ApiHost
    {
        /// <summary>
        /// 构建 WebApplication（不启动）。
        /// </summary>
        /// <param name="options">全局配置</param>
        /// <param name="configureAdditional">额外服务注册（如 WPF 窗口/ViewModel）</param>
        public static WebApplication Build(MjStudioOptions options, Action<IServiceCollection>? configureAdditional = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://{options.ApiHost}:{options.ApiPort}");

            ConfigureServices(builder.Services, options);
            // 显式注册控制器所在程序集：WPF Host 启动时入口程序集是 MjStudio.Host（不含控制器），
            // 默认 AddControllers() 只扫描入口程序集会导致控制器 404。
            builder.Services.AddControllers()
                .AddApplicationPart(typeof(ApiHost).Assembly);
            configureAdditional?.Invoke(builder.Services);

            var app = builder.Build();
            app.MapControllers();
            return app;
        }

        /// <summary>
        /// 注册全部服务（UI 与 API 共享）。
        /// </summary>
        public static void ConfigureServices(IServiceCollection services, MjStudioOptions options)
        {
            // 全局配置
            services.AddSingleton(options);

            // 设置服务（持久化）
            services.AddSingleton<ISettingsService, SettingsService>();

            // 存储与数据库
            services.AddSingleton<ProjectStorageManager>();
            services.AddSingleton<IProjectDbContextFactory, ProjectDbContextFactory>();
            services.AddSingleton<ResourceStorageService>();

            // 当前项目上下文（UI/CLI 选定后写入）
            services.AddSingleton<CurrentProject>();

            // 应用服务（Scoped：与请求/作用域同生命周期）
            services.AddScoped<IProjectService, ProjectService>();
            services.AddScoped<IAssetService, AssetService>();
            services.AddScoped<IStoryService, StoryService>();
            services.AddScoped<IShotService, ShotService>();
            services.AddScoped<IReviewService, ReviewService>();
            services.AddScoped<IResourceService, ResourceService>();

            // HTTP 客户端（ComfyUI 调用）
            services.AddHttpClient();

            // ComfyUI 客户端 + 工作流引擎（单例）
            services.AddSingleton<ComfyUiClient>();
            services.AddSingleton<WorkflowEngine>();
        }
    }
}
