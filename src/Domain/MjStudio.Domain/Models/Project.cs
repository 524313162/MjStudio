using System.ComponentModel.DataAnnotations;
using MjStudio.Domain.Shared;

namespace MjStudio.Domain.Models
{
    /// <summary>
    /// 漫剧项目（对应技能 00_项目\立项.md 的全部立项字段）。
    /// 项目 = 一个数据库文件：&lt;DbRoot&gt;\&lt;Name&gt;.db（项目全部数据都在该 DB 中）；
    /// 媒体资源统一存放：&lt;ResourcesRoot&gt;\&lt;Name&gt;\{images,audio,video,voice,final}。
    /// 路径一律由 ProjectStorageManager 实时计算，不在实体上存路径快照。
    /// </summary>
    public class Project : BaseEntity
    {
        [Required]
        [StringLength(256)]
        public string Name { get; set; } = default!;

        /// <summary>故事名称</summary>
        [StringLength(256)]
        public string? StoryName { get; set; }

        /// <summary>世界观</summary>
        public string? Worldview { get; set; }

        /// <summary>项目简介</summary>
        public string? Description { get; set; }

        /// <summary>总集数</summary>
        public int TotalEpisodes { get; set; }

        /// <summary>每集目标时长（秒，默认 ≥120）</summary>
        public int EpisodeDuration { get; set; } = 120;

        /// <summary>画幅/平台</summary>
        public AspectRatioEnum AspectRatio { get; set; } = AspectRatioEnum.Landscape169;

        /// <summary>目标平台（抖音/B站等）</summary>
        [StringLength(64)]
        public string? TargetPlatform { get; set; }

        /// <summary>题材</summary>
        [StringLength(64)]
        public string? Genre { get; set; }

        /// <summary>目标受众（儿童向/成人向）</summary>
        [StringLength(64)]
        public string? Audience { get; set; }

        /// <summary>美术风格</summary>
        [StringLength(256)]
        public string? ArtStyle { get; set; }

        /// <summary>配音语言</summary>
        [StringLength(64)]
        public string? VoiceLanguage { get; set; } = "中文普通话";

        /// <summary>原创/改编</summary>
        [StringLength(64)]
        public string? OriginalOrAdapted { get; set; } = "原创";

        /// <summary>BGM 风格</summary>
        [StringLength(256)]
        public string? BgmStyle { get; set; }

        /// <summary>是否含片头片尾（默认无）</summary>
        public bool HasOpeningEnding { get; set; }

        /// <summary>交付物说明</summary>
        [StringLength(512)]
        public string? Deliverables { get; set; }

        /// <summary>流程状态（当前进行到的环节）</summary>
        public StageEnum CurrentStage { get; set; } = StageEnum.Init;

        /// <summary>
        /// 项目状态（进行中/已完结）。
        /// 已完结的项目不能再被资产绑定（资产引用项目时过滤掉已完结项目）。
        /// </summary>
        public ProjectStatusEnum Status { get; set; } = ProjectStatusEnum.InProgress;

        /// <summary>封面资源ID</summary>
        public long? CoverResourceId { get; set; }
        public Resource? CoverResource { get; set; }

        public ICollection<Story> Stories { get; set; } = new List<Story>();
        public ICollection<Asset> Assets { get; set; } = new List<Asset>();
        public ICollection<Episode> Episodes { get; set; } = new List<Episode>();
        public ICollection<Review> Reviews { get; set; } = new List<Review>();
    }
}
