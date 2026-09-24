using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MjStudio.Application.Services
{
    public class ProjectService : IProjectService
    {
        private readonly IProjectDbContextFactory _factory;
        private readonly ProjectStorageManager _storage;
        private readonly CurrentProject _current;

        public ProjectService(IProjectDbContextFactory factory, ProjectStorageManager storage, CurrentProject current)
        {
            _factory = factory;
            _storage = storage;
            _current = current;
        }

        public Task<List<string>> ListProjectNamesAsync()
            => Task.FromResult(_storage.ListProjects());

        public async Task<Project?> GetByNameAsync(string name)
        {
            await using var db = _factory.Create(name);
            return await db.Projects.FirstOrDefaultAsync(p => p.Name == name);
        }

        public async Task<Project?> GetByIdAsync(long id)
        {
            var name = _current.Require();
            await using var db = _factory.Create(name);
            return await db.Projects.FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<Project> CreateAsync(Project project)
        {
            // 初始化项目文件夹 + 数据库
            _factory.Initialize(project.Name);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            project.CreatedTime = now;
            project.UpdatedTime = now;

            await using var db = _factory.Create(project.Name);
            db.Projects.Add(project);
            await db.SaveChangesAsync();

            // 设为当前项目
            _current.Set(project.Name);
            return project;
        }

        public async Task UpdateAsync(Project project)
        {
            var name = _current.Require();
            await using var db = _factory.Create(name);
            project.UpdatedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            db.Projects.Update(project);
            await db.SaveChangesAsync();
        }

        public async Task DeleteAsync(string name)
        {
            // 单一全局库：删除项目行（级联删除其故事/集数/评审/资源；资产为多对多，ProjectId 置空保留）
            await using var db = _factory.Create(name);
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Name == name);
            if (project is not null)
            {
                db.Projects.Remove(project);
                await db.SaveChangesAsync();
            }

            // 删除项目资源目录（媒体文件）
            var resDir = _storage.GetResourcesDir(name);
            if (Directory.Exists(resDir))
            {
                try { Directory.Delete(resDir, recursive: true); } catch { /* 忽略 */ }
            }
            // 清理旧版项目文件夹（若存在）
            var legacyDir = _storage.GetLegacyProjectDir(name);
            if (Directory.Exists(legacyDir))
            {
                try { Directory.Delete(legacyDir, recursive: true); } catch { /* 忽略 */ }
            }
            if (_current.Name == name) _current.Clear();
        }
    }
}
