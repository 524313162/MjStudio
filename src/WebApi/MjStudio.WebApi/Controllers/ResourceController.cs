using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace MjStudio.WebApi.Controllers
{
    /// <summary>
    /// 资源控制器（获取资源文件 / 上传资源）
    /// </summary>
    [ApiController]
    [Route("api/resources")]
    public class ResourceController : ControllerBase
    {
        private readonly IResourceService _resources;

        public ResourceController(IResourceService resources) => _resources = resources;

        /// <summary>列出当前项目全部资源</summary>
        [HttpGet]
        public async Task<IActionResult> List()
        {
            var pid = await _resources.GetProjectIdAsync();
            return Ok(await _resources.GetByProjectAsync(pid));
        }

        /// <summary>下载资源文件</summary>
        [HttpGet("{id:long}/file")]
        public async Task<IActionResult> Download(long id)
        {
            var bytes = await _resources.ReadAsync(id);
            var path = await _resources.GetAbsolutePathAsync(id);
            if (bytes is null || path is null)
                return NotFound(new { message = $"资源 {id} 不存在" });
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var contentType = ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".mp4" => "video/mp4",
                ".wav" => "audio/wav",
                ".mp3" => "audio/mpeg",
                _ => "application/octet-stream"
            };
            return File(bytes, contentType, Path.GetFileName(path));
        }

        /// <summary>上传资源文件（multipart）</summary>
        [HttpPost("upload")]
        [RequestSizeLimit(500_000_000)]
        public async Task<IActionResult> Upload(
            [FromForm] string subDir,
            [FromForm] string fileName,
            [FromForm] string mediaType,
            [FromForm] ResourcePurposeEnum purpose,
            [FromForm] IFormFile file)
        {
            var pid = await _resources.GetProjectIdAsync();
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var resource = await _resources.SaveAsync(pid, ms.ToArray(), subDir, fileName, mediaType, purpose);
            return Ok(resource);
        }

        /// <summary>删除资源</summary>
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            await _resources.DeleteAsync(id);
            return Ok(new { message = "已删除" });
        }
    }
}
