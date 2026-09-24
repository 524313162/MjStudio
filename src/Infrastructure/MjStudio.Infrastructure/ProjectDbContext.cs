using Microsoft.EntityFrameworkCore;
using MjStudio.Domain.Models;

namespace MjStudio.Infrastructure
{
    /// <summary>
    /// 漫剧项目数据库上下文（每个项目一个 SQLite 库）
    /// </summary>
    public class ProjectDbContext : DbContext, IProjectDbContext
    {
        public ProjectDbContext(DbContextOptions<ProjectDbContext> options) : base(options) { }

        public DbSet<Project> Projects => Set<Project>();
        public DbSet<Story> Stories => Set<Story>();
        public DbSet<StoryChapter> StoryChapters => Set<StoryChapter>();
        public DbSet<Asset> Assets => Set<Asset>();
        public DbSet<AssetProjectRef> AssetProjectRefs => Set<AssetProjectRef>();
        public DbSet<Episode> Episodes => Set<Episode>();
        public DbSet<Shot> Shots => Set<Shot>();
        public DbSet<ShotAssetRef> ShotAssetRefs => Set<ShotAssetRef>();
        public DbSet<Resource> Resources => Set<Resource>();
        public DbSet<Review> Reviews => Set<Review>();
        public DbSet<ApiProvider> ApiProviders => Set<ApiProvider>();
        public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // ===== Project 关系 =====
            modelBuilder.Entity<Project>()
                .HasMany(p => p.Stories).WithOne(s => s.Project)
                .HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Project>()
                .HasMany(p => p.Assets).WithOne(a => a.Project)
                .HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.SetNull);

            // ===== Asset <-> Project（多对多，经 AssetProjectRef）=====
            modelBuilder.Entity<AssetProjectRef>()
                .HasOne(r => r.Asset).WithMany(a => a.ProjectRefs)
                .HasForeignKey(r => r.AssetId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AssetProjectRef>()
                .HasOne(r => r.Project).WithMany()
                .HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AssetProjectRef>()
                .HasIndex(r => new { r.AssetId, r.ProjectId }).IsUnique();

            // ===== Asset 自引用（子资产：角色声音/变装、场景子面）=====
            modelBuilder.Entity<Asset>()
                .HasOne(a => a.ParentAsset).WithMany(a => a.Children)
                .HasForeignKey(a => a.ParentAssetId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Project>()
                .HasMany(p => p.Episodes).WithOne(e => e.Project)
                .HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Project>()
                .HasMany(p => p.Reviews).WithOne(r => r.Project)
                .HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Project>()
                .HasOne(p => p.CoverResource).WithMany()
                .HasForeignKey(p => p.CoverResourceId).OnDelete(DeleteBehavior.SetNull);

            // ===== Story -> StoryChapter =====
            modelBuilder.Entity<Story>()
                .HasMany(s => s.Chapters).WithOne(c => c.Story)
                .HasForeignKey(c => c.StoryId).OnDelete(DeleteBehavior.Cascade);

            // ===== Episode -> Shot =====
            modelBuilder.Entity<Episode>()
                .HasMany(e => e.Shots).WithOne(s => s.Episode)
                .HasForeignKey(s => s.EpisodeId).OnDelete(DeleteBehavior.Cascade);

            // ===== Shot -> ShotAssetRef =====
            modelBuilder.Entity<Shot>()
                .HasMany(s => s.AssetRefs).WithOne(r => r.Shot)
                .HasForeignKey(r => r.ShotId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ShotAssetRef>()
                .HasOne(r => r.Asset).WithMany()
                .HasForeignKey(r => r.AssetId).OnDelete(DeleteBehavior.Cascade);

            // ===== Asset -> Resource =====
            modelBuilder.Entity<Asset>()
                .HasOne(a => a.Resource).WithMany()
                .HasForeignKey(a => a.ResourceId).OnDelete(DeleteBehavior.SetNull);

            // ===== Shot -> Resource =====
            modelBuilder.Entity<Shot>()
                .HasOne(s => s.VideoResource).WithMany()
                .HasForeignKey(s => s.VideoResourceId).OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Shot>()
                .HasOne(s => s.UpscaledVideoResource).WithMany()
                .HasForeignKey(s => s.UpscaledVideoResourceId).OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Shot>()
                .HasOne(s => s.DubbedVideoResource).WithMany()
                .HasForeignKey(s => s.DubbedVideoResourceId).OnDelete(DeleteBehavior.SetNull);

            // ===== Resource -> Project =====
            modelBuilder.Entity<Resource>()
                .HasOne(r => r.Project).WithMany()
                .HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);

            // ===== 唯一约束 =====
            modelBuilder.Entity<Project>().HasIndex(p => p.Name).IsUnique();
            modelBuilder.Entity<Asset>().HasIndex(a => new { a.ProjectId, a.AssetType, a.Name });
            modelBuilder.Entity<Shot>().HasIndex(s => new { s.EpisodeId, s.ShotNo });
            modelBuilder.Entity<Review>().HasIndex(r => new { r.ProjectId, r.Stage }).IsUnique();

            // ===== 字符串长度 =====
            modelBuilder.Entity<Project>().Property(p => p.Name).HasMaxLength(256);
            modelBuilder.Entity<Project>().Property(p => p.ArtStyle).HasMaxLength(256);
            modelBuilder.Entity<Project>().Property(p => p.Genre).HasMaxLength(64);
            modelBuilder.Entity<Project>().Property(p => p.TargetPlatform).HasMaxLength(64);
            modelBuilder.Entity<Story>().Property(s => s.Title).HasMaxLength(512);
            modelBuilder.Entity<StoryChapter>().Property(c => c.ChapterName).HasMaxLength(256);
            modelBuilder.Entity<Asset>().Property(a => a.Name).HasMaxLength(256);
            modelBuilder.Entity<Resource>().Property(r => r.RelativePath).HasMaxLength(1024);
            modelBuilder.Entity<Resource>().Property(r => r.FileName).HasMaxLength(512);
            modelBuilder.Entity<Shot>().Property(s => s.Title).HasMaxLength(256);
            modelBuilder.Entity<Shot>().Property(s => s.ShotSize).HasMaxLength(32);
            modelBuilder.Entity<Shot>().Property(s => s.CameraFacing).HasMaxLength(64);
            modelBuilder.Entity<Shot>().Property(s => s.Timecode).HasMaxLength(64);
            modelBuilder.Entity<Shot>().Property(s => s.Transition).HasMaxLength(64);
            modelBuilder.Entity<Episode>().Property(e => e.Name).HasMaxLength(256);
            modelBuilder.Entity<PromptTemplate>().Property(pt => pt.Name).HasMaxLength(256);
            modelBuilder.Entity<ApiProvider>().Property(ap => ap.Name).HasMaxLength(256);
            modelBuilder.Entity<ApiProvider>().Property(ap => ap.ApiUrl).HasMaxLength(512);
            modelBuilder.Entity<ApiProvider>().Property(ap => ap.ApiKey).HasMaxLength(1024);
        }
    }
}
