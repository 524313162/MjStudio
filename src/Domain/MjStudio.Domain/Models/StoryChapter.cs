using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Models
{
    /// <summary>
    /// 剧本章节/段落（可选细分）
    /// </summary>
    public class StoryChapter : BaseEntity
    {
        public long StoryId { get; set; }
        public Story? Story { get; set; }

        [Required]
        [StringLength(256)]
        public string ChapterName { get; set; } = default!;

        public string? Content { get; set; }

        public int Order { get; set; }
    }
}
