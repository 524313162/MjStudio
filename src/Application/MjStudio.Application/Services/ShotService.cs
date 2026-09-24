using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using MjStudio.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MjStudio.Application.Services
{
    public class ShotService : IShotService
    {
        private readonly IProjectDbContextFactory _factory;
        private readonly CurrentProject _current;

        public ShotService(IProjectDbContextFactory factory, CurrentProject current)
        {
            _factory = factory;
            _current = current;
        }

        public async Task<List<Episode>> GetEpisodesAsync(long projectId)
        {
            await using var db = _factory.Create(_current.Require());
            return await db.Episodes.Where(e => e.ProjectId == projectId).OrderBy(e => e.EpisodeNo).ToListAsync();
        }

        public async Task<Episode?> GetEpisodeAsync(long projectId, int episodeNo)
        {
            await using var db = _factory.Create(_current.Require());
            return await db.Episodes.Include(e => e.Shots).ThenInclude(s => s.AssetRefs)
                .FirstOrDefaultAsync(e => e.ProjectId == projectId && e.EpisodeNo == episodeNo);
        }

        public async Task<Episode> CreateEpisodeAsync(Episode episode)
        {
            await using var db = _factory.Create(_current.Require());
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            episode.CreatedTime = now;
            episode.UpdatedTime = now;
            db.Episodes.Add(episode);
            await db.SaveChangesAsync();
            return episode;
        }

        public async Task UpdateEpisodeAsync(Episode episode)
        {
            await using var db = _factory.Create(_current.Require());
            episode.UpdatedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            db.Episodes.Update(episode);
            await db.SaveChangesAsync();
        }

        public async Task<List<Shot>> GetShotsAsync(long episodeId)
        {
            await using var db = _factory.Create(_current.Require());
            return await db.Shots.Include(s => s.AssetRefs).ThenInclude(r => r.Asset)
                .Where(s => s.EpisodeId == episodeId).OrderBy(s => s.ShotNo).ToListAsync();
        }

        public async Task<Shot?> GetShotAsync(long id)
        {
            await using var db = _factory.Create(_current.Require());
            return await db.Shots.Include(s => s.AssetRefs).ThenInclude(r => r.Asset)
                .Include(s => s.VideoResource)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<Shot> CreateShotAsync(Shot shot)
        {
            await using var db = _factory.Create(_current.Require());
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            shot.CreatedTime = now;
            shot.UpdatedTime = now;
            db.Shots.Add(shot);
            await db.SaveChangesAsync();
            return shot;
        }

        public async Task UpdateShotAsync(Shot shot)
        {
            await using var db = _factory.Create(_current.Require());
            shot.UpdatedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            db.Shots.Update(shot);
            await db.SaveChangesAsync();
        }

        public async Task DeleteShotAsync(long id)
        {
            await using var db = _factory.Create(_current.Require());
            var shot = await db.Shots.FirstOrDefaultAsync(s => s.Id == id);
            if (shot is null) return;
            db.Shots.Remove(shot);
            await db.SaveChangesAsync();
        }

        public async Task UpdateVideoPromptsAsync(long shotId, string? promptCn, string? promptEn, string? negativeEn)
        {
            await using var db = _factory.Create(_current.Require());
            var shot = await db.Shots.FirstAsync(s => s.Id == shotId);
            shot.VideoPromptCn = promptCn;
            shot.VideoPromptEn = promptEn;
            shot.VideoNegativePromptEn = negativeEn;
            shot.UpdatedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await db.SaveChangesAsync();
        }

        public async Task<Resource> AttachVideoAsync(long shotId, string relativePath, ResourcePurposeEnum purpose)
        {
            await using var db = _factory.Create(_current.Require());
            var shot = await db.Shots.FirstAsync(s => s.Id == shotId);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var projectId = shot.Episode!.ProjectId;

            long? targetId = purpose switch
            {
                ResourcePurposeEnum.UpscaledVideo => shot.UpscaledVideoResourceId,
                ResourcePurposeEnum.DubbingClip => shot.DubbedVideoResourceId,
                _ => shot.VideoResourceId
            };

            var resource = targetId is long rid
                ? await db.Resources.FirstOrDefaultAsync(r => r.Id == rid)
                : null;
            if (resource is null)
            {
                resource = new Resource
                {
                    ProjectId = projectId,
                    MediaType = "video",
                    Purpose = purpose,
                    RelativePath = relativePath,
                    FileName = Path.GetFileName(relativePath),
                    CreatedTime = now,
                    UpdatedTime = now
                };
                db.Resources.Add(resource);
            }
            else
            {
                resource.RelativePath = relativePath;
                resource.FileName = Path.GetFileName(relativePath);
                resource.Purpose = purpose;
                resource.UpdatedTime = now;
            }

            switch (purpose)
            {
                case ResourcePurposeEnum.UpscaledVideo: shot.UpscaledVideoResourceId = resource.Id; break;
                case ResourcePurposeEnum.DubbingClip: shot.DubbedVideoResourceId = resource.Id; break;
                default: shot.VideoResourceId = resource.Id; break;
            }
            shot.UpdatedTime = now;
            await db.SaveChangesAsync();
            return resource;
        }

        public async Task SetAssetRefsAsync(long shotId, List<(long AssetId, int MediaIndex, string RefType)> refs)
        {
            await using var db = _factory.Create(_current.Require());
            var shot = await db.Shots.Include(s => s.AssetRefs).FirstAsync(s => s.Id == shotId);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 清除旧引用
            db.ShotAssetRefs.RemoveRange(shot.AssetRefs);
            shot.AssetRefs.Clear();

            foreach (var (assetId, mediaIndex, refType) in refs)
            {
                shot.AssetRefs.Add(new ShotAssetRef
                {
                    AssetId = assetId,
                    MediaIndex = mediaIndex,
                    RefType = refType,
                    CreatedTime = now,
                    UpdatedTime = now
                });
            }
            shot.RefImageCount = refs.Count;
            shot.UpdatedTime = now;
            await db.SaveChangesAsync();
        }
    }
}
