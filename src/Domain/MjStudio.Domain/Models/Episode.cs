using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Models
{
    /// <summary>
    /// 集（对应 05_视频\第N集 等按集组织的产出）
    /// </summary>
    public class Episode : BaseEntity
    {
        public long ProjectId { get; set; }
        public Project? Project { get; set; }

        /// <summary>集数</summary>
        public int EpisodeNo { get; set; }

        [Required]
        [StringLength(256)]
        public string Name { get; set; } = default!;

        /// <summary>本集总时长（秒）</summary>
        public int? Duration { get; set; }

        /// <summary>本集资产白名单（角色/场景/道具/BGM/音效 名称，JSON 或文本）</summary>
        public string? AssetWhitelist { get; set; }

        /// <summary>本集同场景机位切换表（Markdown 表格）</summary>
        public string? CameraSwitchTable { get; set; }

        /// <summary>本集音频出现点表（BGM/音效精确出现点）</summary>
        public string? AudioCueTable { get; set; }

        /// <summary>台词清单（第N集台词.json 内容）</summary>
        public string? DialogueList { get; set; }

        public ICollection<Shot> Shots { get; set; } = new List<Shot>();
    }
}
