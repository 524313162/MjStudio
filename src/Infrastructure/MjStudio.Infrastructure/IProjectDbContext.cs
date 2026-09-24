namespace MjStudio.Infrastructure
{
    /// <summary>
    /// 数据库上下文接口（便于测试与替换）
    /// </summary>
    public interface IProjectDbContext
    {
        Microsoft.EntityFrameworkCore.DbSet<Domain.Models.Project> Projects { get; }
        Microsoft.EntityFrameworkCore.DbSet<Domain.Models.Story> Stories { get; }
        Microsoft.EntityFrameworkCore.DbSet<Domain.Models.StoryChapter> StoryChapters { get; }
        Microsoft.EntityFrameworkCore.DbSet<Domain.Models.Asset> Assets { get; }
        Microsoft.EntityFrameworkCore.DbSet<Domain.Models.AssetProjectRef> AssetProjectRefs { get; }
        Microsoft.EntityFrameworkCore.DbSet<Domain.Models.Episode> Episodes { get; }
        Microsoft.EntityFrameworkCore.DbSet<Domain.Models.Shot> Shots { get; }
        Microsoft.EntityFrameworkCore.DbSet<Domain.Models.ShotAssetRef> ShotAssetRefs { get; }
        Microsoft.EntityFrameworkCore.DbSet<Domain.Models.Resource> Resources { get; }
        Microsoft.EntityFrameworkCore.DbSet<Domain.Models.Review> Reviews { get; }
        Microsoft.EntityFrameworkCore.DbSet<Domain.Models.ApiProvider> ApiProviders { get; }
        Microsoft.EntityFrameworkCore.DbSet<Domain.Models.PromptTemplate> PromptTemplates { get; }

        Task<int> SaveChangesAsync(CancellationToken ct = default);
        ValueTask DisposeAsync();
    }
}
