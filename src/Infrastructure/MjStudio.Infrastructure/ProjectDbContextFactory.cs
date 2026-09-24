using Microsoft.EntityFrameworkCore;
using MjStudio.Domain.Shared;

namespace MjStudio.Infrastructure
{
    /// <summary>
    /// 项目数据库上下文工厂：按项目名创建指向该项目 SQLite 库的上下文。
    /// </summary>
    public interface IProjectDbContextFactory
    {
        /// <summary>为指定项目创建上下文（调用方负责 Dispose）</summary>
        ProjectDbContext Create(string projectName);

        /// <summary>初始化项目数据库（建库 + 建表）</summary>
        void Initialize(string projectName);
    }

    public class ProjectDbContextFactory : IProjectDbContextFactory
    {
        private readonly ProjectStorageManager _storage;

        public ProjectDbContextFactory(ProjectStorageManager storage) => _storage = storage;

        public ProjectDbContext Create(string projectName)
        {
            // 单一全局库：所有项目共用 <DbRoot>\mjstudio.db（projectName 仅用于资源目录等场景）
            _storage.EnsureGlobalDb();
            var options = new DbContextOptionsBuilder<ProjectDbContext>()
                .UseSqlite($"Data Source={_storage.GetGlobalDbPath()}")
                .Options;
            return new ProjectDbContext(options);
        }

        public void Initialize(string projectName)
        {
            _storage.InitializeProject(projectName);
            using var db = Create(projectName);
            db.Database.EnsureCreated();
        }
    }
}
