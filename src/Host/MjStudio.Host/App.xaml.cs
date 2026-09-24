using System.Windows;
using MjStudio.Application.Services;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Shared;
using MjStudio.Host.Services;
using MjStudio.Host.ViewModels;
using MjStudio.WebApi;
using Microsoft.Extensions.DependencyInjection;

namespace MjStudio.Host
{
    /// <summary>
    /// 应用入口：构建 DI → 启动内置 API → 显示主窗口（启动时选择/加载项目）。
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public IServiceProvider Services { get; private set; } = default!;
        public ApiServerManager ApiServer { get; } = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 全局异常捕获
            DispatcherUnhandledException += (_, args) =>
            {
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(System.AppContext.BaseDirectory, "startup.log"),
                        $"\n[UNHANDLED] {DateTime.Now:O}\n{args.Exception}\n");
                }
                catch { }
                MessageBox.Show("发生未处理的异常：\n" + args.Exception.Message,
                    "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            // 1. 读取设置
            var settings = new SettingsService();
            var options = settings.Get();

            // 2. 启动内置 API（Kestrel 内嵌，UI 与 API 共享 DI 容器）
            ApiServer.Start(options, services =>
            {
                // 注册 ApiServerManager 实例（MainViewModel 依赖它做重启 API）
                services.AddSingleton<ApiServerManager>(ApiServer);
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            });
            Services = ApiServer.Services;

            // 3. 显示主窗口
            try
            {
                var window = Services.GetRequiredService<MainWindow>();
                MainWindow = window;
                window.Show();
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.AppContext.BaseDirectory, "startup.log"),
                    $"[OK] {DateTime.Now:O} window shown");
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.AppContext.BaseDirectory, "startup.log"),
                    $"[ERR] {DateTime.Now:O}\n{ex}");
                throw;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ApiServer.Stop();
            base.OnExit(e);
        }
    }
}

