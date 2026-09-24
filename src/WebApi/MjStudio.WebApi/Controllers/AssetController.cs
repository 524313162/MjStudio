using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using MjStudio.WebApi.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace MjStudio.WebApi.Controllers
{
    /// <summary>
    /// 资产控制器（角色/场景/道具/BGM/音乐/音效，含 CN/EN 提示词）
    /// </summary>
    [ApiController]
    [Route("api/assets")]
    public class AssetController : ControllerBase
    {
        private readonly IAssetService _assets;
        private readonly IResourceService _resources;

        public AssetController(IAssetService assets, IResourceService resources)
        {
            _assets = assets;
            _resources = resources;
        }

        private Task<long> ProjectIdAsync() => _resources.GetProjectIdAsync();

        /// <summary>按类型列出资产</summary>
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] AssetTypeEnum? type)
        {
            var pid = await ProjectIdAsync();
            return type is null
                ? Ok(await _assets.GetAllAsync(pid))
                : Ok(await _assets.GetByTypeAsync(pid, type.Value));
        }

        /// <summary>获取单个资产</summary>
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id)
        {
            var asset = await _assets.GetByIdAsync(id);
            return asset is null ? NotFound() : Ok(asset);
        }

        /// <summary>创建/更新资产</summary>
        [HttpPost]
        public async Task<IActionResult> Upsert([FromBody] AssetUpsertRequest req)
        {
            var pid = await ProjectIdAsync();
            var asset = req.Id is long id ? await _assets.GetByIdAsync(id) : null;
            if (asset is null)
            {
                asset = new Asset { ProjectId = pid };
            }
            Apply(req, asset);
            if (asset.Id == 0)
                return Ok(await _assets.CreateAsync(asset));
            await _assets.UpdateAsync(asset);
            return Ok(asset);
        }

        /// <summary>更新资产提示词（单个正向 + 单个反向）</summary>
        [HttpPut("{id:long}/prompts")]
        public async Task<IActionResult> UpdatePrompts(long id, [FromBody] PromptUpdateRequest req)
        {
            await _assets.UpdatePromptsAsync(id, req.Prompt, req.NegativePrompt);
            return Ok(await _assets.GetByIdAsync(id));
        }

        /// <summary>设置资产被哪些项目引用（多对多，全量覆盖）</summary>
        [HttpPut("{id:long}/refs")]
        public async Task<IActionResult> SetRefs(long id, [FromBody] AssetRefsRequest req)
        {
            await _assets.SetProjectRefsAsync(id, req.ProjectIds ?? new List<long>());
            return Ok(await _assets.GetByIdAsync(id));
        }

        /// <summary>关联资源到资产（图片/音频等）</summary>
        [HttpPut("{id:long}/resource")]
        public async Task<IActionResult> AttachResource(long id,
            [FromQuery] string relativePath,
            [FromQuery] string mediaType,
            [FromQuery] ResourcePurposeEnum purpose,
            [FromQuery] float? duration = null)
        {
            var resource = await _assets.AttachResourceAsync(id, relativePath, mediaType, purpose, duration);
            return Ok(resource);
        }

        /// <summary>删除资产</summary>
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            await _assets.DeleteAsync(id);
            return Ok(new { message = "已删除" });
        }

        private static void Apply(AssetUpsertRequest req, Asset asset)
        {
            asset.AssetType = req.AssetType;
            asset.Name = req.Name;
            asset.Description = req.Description;
            asset.Prompt = req.Prompt;
            asset.NegativePrompt = req.NegativePrompt;
            asset.Order = req.Order;
        }
    }
}
