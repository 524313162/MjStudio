using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using MjStudio.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MjStudio.Application.Services
{
    public class AssetService : IAssetService
    {
        private readonly IProjectDbContextFactory _factory;
        private readonly CurrentProject _current;

        public AssetService(IProjectDbContextFactory factory, CurrentProject current)
        {
            _factory = factory;
            _current = current;
        }

        private async Task<ProjectDbContext> DbAsync()
        {
            var db = _factory.Create(_current.Require());
            return db;
        }

        public async Task<List<Asset>> GetByTypeAsync(long projectId, AssetTypeEnum type)
        {
            await using var db = await DbAsync();
            return await db.Assets
                .Include(a => a.Resource)
                .Include(a => a.Children).ThenInclude(c => c.Resource)
                .Where(a => a.ProjectId == projectId && a.AssetType == type)
                .OrderBy(a => a.Order).ThenBy(a => a.Name)
                .ToListAsync();
        }

        public async Task<List<Asset>> GetAllAsync(long projectId)
        {
            await using var db = await DbAsync();
            return await db.Assets
                .Include(a => a.Resource)
                .Include(a => a.Children).ThenInclude(c => c.Resource)
                .Where(a => a.ProjectId == projectId)
                .OrderBy(a => a.AssetType).ThenBy(a => a.Order).ThenBy(a => a.Name)
                .ToListAsync();
        }

        /// <summary>获取全部资产（跨项目，含引用项目集合），可按类型过滤</summary>
        public async Task<List<Asset>> GetAllWithRefsAsync(AssetTypeEnum? type = null)
        {
            await using var db = await DbAsync();
            var q = db.Assets
                .Include(a => a.Resource)
                .Include(a => a.Children).ThenInclude(c => c.Resource)
                .Include(a => a.ProjectRefs).ThenInclude(r => r.Project)
                .AsQueryable();
            if (type is not null) q = q.Where(a => a.AssetType == type.Value);
            return await q
                .OrderBy(a => a.AssetType).ThenBy(a => a.Order).ThenBy(a => a.Name)
                .ToListAsync();
        }

        /// <summary>获取某项目引用的全部资产（含通用资产）</summary>
        public async Task<List<Asset>> GetByProjectAsync(long projectId)
        {
            await using var db = await DbAsync();
            return await db.Assets
                .Include(a => a.Resource)
                .Include(a => a.Children).ThenInclude(c => c.Resource)
                .Include(a => a.ProjectRefs).ThenInclude(r => r.Project)
                .Where(a => a.ProjectRefs.Any(r => r.ProjectId == projectId))
                .OrderBy(a => a.AssetType).ThenBy(a => a.Order).ThenBy(a => a.Name)
                .ToListAsync();
        }

        /// <summary>设置资产被哪些项目引用（多对多，全量覆盖）</summary>
        public async Task SetProjectRefsAsync(long assetId, List<long> projectIds)
        {
            await using var db = await DbAsync();
            var refs = await db.AssetProjectRefs.Where(r => r.AssetId == assetId).ToListAsync();
            foreach (var r in refs) db.AssetProjectRefs.Remove(r);

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var pid in projectIds.Distinct())
            {
                db.AssetProjectRefs.Add(new AssetProjectRef
                {
                    AssetId = assetId,
                    ProjectId = pid,
                    CreatedTime = now,
                    UpdatedTime = now
                });
            }
            await db.SaveChangesAsync();
        }

        public async Task<Asset?> GetByIdAsync(long id)
        {
            await using var db = await DbAsync();
            return await db.Assets.Include(a => a.Resource)
                .FirstOrDefaultAsync(a => a.Id == id);
        }

        public async Task<Asset?> FindByNameAsync(long projectId, AssetTypeEnum type, string name)
        {
            await using var db = await DbAsync();
            return await db.Assets.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.AssetType == type && a.Name == name);
        }

        public async Task<Asset> CreateAsync(Asset asset)
        {
            await using var db = await DbAsync();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            asset.CreatedTime = now;
            asset.UpdatedTime = now;
            db.Assets.Add(asset);
            await db.SaveChangesAsync();
            return asset;
        }

        public async Task UpdateAsync(Asset asset)
        {
            await using var db = await DbAsync();
            asset.UpdatedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            db.Assets.Update(asset);
            await db.SaveChangesAsync();
        }

        public async Task DeleteAsync(long id)
        {
            await using var db = await DbAsync();
            var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id);
            if (asset is null) return;
            db.Assets.Remove(asset);
            await db.SaveChangesAsync();
        }

        public async Task UpdatePromptsAsync(long assetId, string? prompt, string? negativePrompt)
        {
            await using var db = await DbAsync();
            var asset = await db.Assets.FirstAsync(a => a.Id == assetId);
            asset.Prompt = prompt;
            asset.NegativePrompt = negativePrompt;
            asset.UpdatedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await db.SaveChangesAsync();
        }

        public async Task<Resource> AttachResourceAsync(long assetId, string relativePath, string mediaType, ResourcePurposeEnum purpose, float? duration = null)
        {
            await using var db = await DbAsync();
            var asset = await db.Assets.FirstAsync(a => a.Id == assetId);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var resource = asset.ResourceId is long rid
                ? await db.Resources.FirstOrDefaultAsync(r => r.Id == rid)
                : null;
            if (resource is null)
            {
                resource = new Resource
                {
                    ProjectId = asset.ProjectId ?? 0,
                    MediaType = mediaType,
                    Purpose = purpose,
                    RelativePath = relativePath,
                    FileName = Path.GetFileName(relativePath),
                    Duration = duration,
                    CreatedTime = now,
                    UpdatedTime = now
                };
                db.Resources.Add(resource);
            }
            else
            {
                resource.RelativePath = relativePath;
                resource.FileName = Path.GetFileName(relativePath);
                resource.MediaType = mediaType;
                resource.Purpose = purpose;
                resource.Duration = duration;
                resource.UpdatedTime = now;
            }

            asset.ResourceId = resource.Id;
            asset.UpdatedTime = now;
            await db.SaveChangesAsync();
            return resource;
        }
    }
}
