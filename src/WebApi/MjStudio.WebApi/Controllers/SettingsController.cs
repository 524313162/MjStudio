using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace MjStudio.WebApi.Controllers
{
    /// <summary>
    /// 设置控制器（ComfyUI 地址、API 端口、项目根目录等）
    /// </summary>
    [ApiController]
    [Route("api/settings")]
    public class SettingsController : ControllerBase
    {
        private readonly ISettingsService _settings;

        public SettingsController(ISettingsService settings) => _settings = settings;

        /// <summary>获取设置</summary>
        [HttpGet]
        public IActionResult Get() => Ok(_settings.Get());

        /// <summary>更新设置（端口变更需调用方重启 API 生效）</summary>
        [HttpPut]
        public IActionResult Update([FromBody] MjStudioOptions options)
        {
            _settings.Update(options);
            return Ok(options);
        }
    }
}
