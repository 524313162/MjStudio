using MjStudio.Domain.Shared;

namespace MjStudio.Application.Shared.Services
{
    /// <summary>
    /// 设置服务契约（全局配置：ComfyUI 地址、API 端口、项目根目录等）
    /// </summary>
    public interface ISettingsService
    {
        MjStudioOptions Get();
        void Update(MjStudioOptions options);
    }
}
