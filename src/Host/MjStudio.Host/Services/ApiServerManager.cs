using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Shared;
using MjStudio.WebApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace MjStudio.Host.Services
{
    /// <summary>
    /// 内置 API 服务器管理器：管理内嵌 Kestrel 的生命周期（启动/停止/重启）。
    /// 端口可在设置中修改，修改后调用 Restart 生效。
    /// UI 与 API 共享同一 DI 容器（WebApplication.Services）。
    /// </summary>
    public class ApiServerManager
    {
        private WebApplication? _app;
        private CancellationTokenSource? _cts;

        /// <summary>API 是否运行中</summary>
        public bool IsRunning { get; private set; }

        /// <summary>当前监听地址</summary>
        public string? Url { get; private set; }

        /// <summary>全局 DI 容器（UI 与 API 共享）</summary>
        public IServiceProvider Services { get; private set; } = default!;

        /// <summary>
        /// 启动 API 服务器。
        /// </summary>
        public void Start(MjStudioOptions options, Action<IServiceCollection>? configureAdditional = null)
        {
            Stop();
            _app = ApiHost.Build(options, configureAdditional);
            Services = _app.Services;
            _cts = new CancellationTokenSource();
            Url = $"http://{options.ApiHost}:{options.ApiPort}";
            IsRunning = true;
            _ = _app.StartAsync(_cts.Token);
        }

        /// <summary>
        /// 重启 API 服务器（端口变更后调用）。
        /// </summary>
        public void Restart(MjStudioOptions options)
        {
            Stop();
            Start(options);
        }

        /// <summary>
        /// 停止 API 服务器。
        /// </summary>
        public void Stop()
        {
            if (_app is not null)
            {
                try { _app.StopAsync().GetAwaiter().GetResult(); } catch { /* 忽略 */ }
                try { _app.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* 忽略 */ }
                _app = null;
            }
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }
}
