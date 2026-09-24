using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Models
{
    /// <summary>
    /// 提示词模板（资产/视频/音乐/音效 各环节的 CN/EN 双文件两段式模板）
    /// </summary>
    public class PromptTemplate : BaseEntity
    {
        [Required]
        [StringLength(256)]
        public string Name { get; set; } = default!;

        /// <summary>模板类型（character/scene/prop/bgm/music/sfx/video）</summary>
        [StringLength(64)]
        public string? TemplateType { get; set; }

        /// <summary>中文模板</summary>
        public string? TemplateCn { get; set; }

        /// <summary>英文模板</summary>
        public string? TemplateEn { get; set; }

        /// <summary>说明</summary>
        public string? Description { get; set; }
    }
}
