using MjStudio.Application.Services;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Shared;
using MjStudio.WebApi;

// 独立运行入口（CLI 模式）：dotnet run --project src/WebApi/MjStudio.WebApi
// 正常由 WPF Host 内嵌启动；此入口用于纯 API/CLI 场景。
var settings = new SettingsService();
var options = settings.Get();

var app = ApiHost.Build(options);
app.Run();
