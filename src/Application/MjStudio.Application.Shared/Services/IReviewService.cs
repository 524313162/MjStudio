using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;

namespace MjStudio.Application.Shared.Services
{
    /// <summary>
    /// 评审服务契约（评审保留给 agent，经 API 读写）
    /// </summary>
    public interface IReviewService
    {
        /// <summary>获取某环节评审记录</summary>
        Task<Review?> GetByStageAsync(long projectId, StageEnum stage);

        /// <summary>获取项目全部评审记录</summary>
        Task<List<Review>> GetAllAsync(long projectId);

        /// <summary>写入/更新评审结论（agent 调用）</summary>
        Task<Review> UpsertAsync(Review review);

        /// <summary>
        /// 记录一次打回（打回计数 +1；超过 5 次自动升级 escalated）
        /// </summary>
        Task<Review> RecordRevisionAsync(long projectId, StageEnum stage, string? issues);
    }
}
