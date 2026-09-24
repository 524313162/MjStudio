using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;

namespace MjStudio.Application.Shared.Services
{
    /// <summary>
    /// 分镜服务契约（集 + 镜头 + 资产引用）
    /// </summary>
    public interface IShotService
    {
        Task<List<Episode>> GetEpisodesAsync(long projectId);
        Task<Episode?> GetEpisodeAsync(long projectId, int episodeNo);
        Task<Episode> CreateEpisodeAsync(Episode episode);
        Task UpdateEpisodeAsync(Episode episode);

        Task<List<Shot>> GetShotsAsync(long episodeId);
        Task<Shot?> GetShotAsync(long id);
        Task<Shot> CreateShotAsync(Shot shot);
        Task UpdateShotAsync(Shot shot);
        Task DeleteShotAsync(long id);

        /// <summary>更新镜头视频提示词（CN/EN 双字段）</summary>
        Task UpdateVideoPromptsAsync(long shotId, string? promptCn, string? promptEn, string? negativeEn);

        /// <summary>为镜头挂载视频资源</summary>
        Task<Resource> AttachVideoAsync(long shotId, string relativePath, ResourcePurposeEnum purpose);

        /// <summary>设置镜头资产引用（参考图排定）</summary>
        Task SetAssetRefsAsync(long shotId, List<(long AssetId, int MediaIndex, string RefType)> refs);
    }
}
