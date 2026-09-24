using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;

namespace MjStudio.Application.Shared.Services
{
    /// <summary>
    /// 资产服务契约（角色/场景/道具/BGM/音乐/音效，含 CN/EN 提示词）
    /// </summary>
    public interface IAssetService
    {
        Task<List<Asset>> GetByTypeAsync(long projectId, AssetTypeEnum type);

        /// <summary>获取全部资产（跨项目，含引用项目集合），可按类型过滤（null=全部）</summary>
        Task<List<Asset>> GetAllWithRefsAsync(AssetTypeEnum? type = null);

        /// <summary>获取某项目引用的全部资产（含通用资产）</summary>
        Task<List<Asset>> GetByProjectAsync(long projectId);

        Task<List<Asset>> GetAllAsync(long projectId);
        Task<Asset?> GetByIdAsync(long id);
        Task<Asset?> FindByNameAsync(long projectId, AssetTypeEnum type, string name);
        Task<Asset> CreateAsync(Asset asset);
        Task UpdateAsync(Asset asset);
        Task DeleteAsync(long id);

        /// <summary>设置资产被哪些项目引用（多对多，全量覆盖）</summary>
        Task SetProjectRefsAsync(long assetId, List<long> projectIds);

        /// <summary>更新资产提示词（单个正向 + 单个反向）</summary>
        Task UpdatePromptsAsync(long assetId, string? prompt, string? negativePrompt);

        /// <summary>为资产挂载主资源（图片/音频）</summary>
        Task<Resource> AttachResourceAsync(long assetId, string relativePath, string mediaType, ResourcePurposeEnum purpose, float? duration = null);
    }
}
