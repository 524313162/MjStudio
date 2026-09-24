using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Models
{
    /// <summary>
    /// 剧本（对应 01_剧本\第N集.md，每集一条）
    /// </summary>
    public class Story : BaseEntity
    {
        public long ProjectId { get; set; }
        public Project? Project { get; set; }

        /// <summary>集数（第N集）</summary>
        public int EpisodeNo { get; set; }

        [Required]
        [StringLength(512)]
        public string Title { get; set; } = default!;

        /// <summary>本集角色列表（来自分镜提取）</summary>
        public string? CharacterList { get; set; }

        /// <summary>剧本正文（Markdown）</summary>
        public string? Content { get; set; }

        /// <summary>本集时长（秒）</summary>
        public int? Duration { get; set; }

        public ICollection<StoryChapter> Chapters { get; set; } = new List<StoryChapter>();
    }
}
