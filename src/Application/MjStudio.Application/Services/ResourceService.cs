using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using MjStudio.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MjStudio.Application.Services
{
    public class ResourceService : IResourceService
    {
        private readonly IProjectDbContextFactory _factory;
        private readonly CurrentProject _current;
        private readonly ResourceStorageService _storage;

        public ResourceService(IProjectDbContextFactory factory, CurrentProject current, ResourceStorageService storage)
        {
            _factory = factory;
            _current = current;
            _storage = storage;
        }

        public async Task<Resource> SaveAsync(long projectId, byte[] bytes, string subDir, string fileName,
            string mediaType, ResourcePurposeEnum purpose, float? duration = null)
        {
            var projectName = _current.Require();
            var (relativePath, _, size) = _storage.Save(projectName, bytes, subDir, fileName, mediaType, purpose);

            await using var db = _factory.Create(projectName);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var resource = new Resource
            {
                ProjectId = projectId,
                MediaType = mediaType,
                Purpose = purpose,
                RelativePath = relativePath,
                FileName = fileName,
                FileSize = size,
                Duration = duration,
                CreatedTime = now,
                UpdatedTime = now
            };
            db.Resources.Add(resource);
            await db.SaveChangesAsync();
            return resource;
        }

        public async Task<byte[]?> ReadAsync(long resourceId)
        {
            var projectName = _current.Require();
            await using var db = _factory.Create(projectName);
            var resource = await db.Resources.FirstOrDefaultAsync(r => r.Id == resourceId);
            if (resource is null) return null;
            return _storage.Read(projectName, resource.RelativePath);
        }

        public async Task<string?> GetAbsolutePathAsync(long resourceId)
        {
            var projectName = _current.Require();
            await using var db = _factory.Create(projectName);
            var resource = await db.Resources.FirstOrDefaultAsync(r => r.Id == resourceId);
            if (resource is null) return null;
            return _storage.GetAbsolutePath(projectName, resource.RelativePath);
        }

        public async Task<List<Resource>> GetByProjectAsync(long projectId)
        {
            await using var db = _factory.Create(_current.Require());
            return await db.Resources.Where(r => r.ProjectId == projectId).OrderByDescending(r => r.CreatedTime).ToListAsync();
        }

        public async Task<long> GetProjectIdAsync()
        {
            var name = _current.Require();
            await using var db = _factory.Create(name);
            return await db.Projects.AsNoTracking().Where(p => p.Name == name).Select(p => p.Id).FirstAsync();
        }

        public async Task DeleteAsync(long resourceId)
        {
            var projectName = _current.Require();
            await using var db = _factory.Create(projectName);
            var resource = await db.Resources.FirstOrDefaultAsync(r => r.Id == resourceId);
            if (resource is null) return;
            _storage.Delete(projectName, resource.RelativePath);
            db.Resources.Remove(resource);
            await db.SaveChangesAsync();
        }
    }
}
