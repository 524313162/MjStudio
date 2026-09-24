using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using MjStudio.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MjStudio.Application.Services
{
    /// <summary>
    /// 评审服务（评审保留给 agent，经 API 读写）。
    /// 打回规则：同一环节最多打回 5 次，第 6 次自动升级 escalated。
    /// </summary>
    public class ReviewService : IReviewService
    {
        private const int MaxRevision = 5;

        private readonly IProjectDbContextFactory _factory;
        private readonly CurrentProject _current;

        public ReviewService(IProjectDbContextFactory factory, CurrentProject current)
        {
            _factory = factory;
            _current = current;
        }

        public async Task<Review?> GetByStageAsync(long projectId, StageEnum stage)
        {
            await using var db = _factory.Create(_current.Require());
            return await db.Reviews.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Stage == stage);
        }

        public async Task<List<Review>> GetAllAsync(long projectId)
        {
            await using var db = _factory.Create(_current.Require());
            return await db.Reviews.Where(r => r.ProjectId == projectId).OrderBy(r => r.Stage).ToListAsync();
        }

        public async Task<Review> UpsertAsync(Review review)
        {
            await using var db = _factory.Create(_current.Require());
            var existing = await db.Reviews.FirstOrDefaultAsync(r => r.ProjectId == review.ProjectId && r.Stage == review.Stage);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (existing is null)
            {
                review.CreatedTime = now;
                review.UpdatedTime = now;
                review.ReviewedTime = now;
                db.Reviews.Add(review);
            }
            else
            {
                existing.Decision = review.Decision;
                existing.Content = review.Content;
                existing.Issues = review.Issues;
                existing.RevisionCount = review.RevisionCount;
                existing.ReviewedTime = now;
                existing.UpdatedTime = now;
            }
            await db.SaveChangesAsync();
            return existing ?? review;
        }

        public async Task<Review> RecordRevisionAsync(long projectId, StageEnum stage, string? issues)
        {
            await using var db = _factory.Create(_current.Require());
            var review = await db.Reviews.FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Stage == stage);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (review is null)
            {
                review = new Review
                {
                    ProjectId = projectId,
                    Stage = stage,
                    CreatedTime = now,
                    UpdatedTime = now
                };
                db.Reviews.Add(review);
            }

            review.RevisionCount += 1;
            review.Issues = issues;
            review.ReviewedTime = now;
            review.UpdatedTime = now;

            // 打回规则：超过 5 次升级
            review.Decision = review.RevisionCount > MaxRevision
                ? ReviewDecisionEnum.Escalated
                : ReviewDecisionEnum.Revision;

            await db.SaveChangesAsync();
            return review;
        }
    }
}
