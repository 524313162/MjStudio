using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Models
{
    /// <summary>
    /// 资源文件（图片/音频/视频等二进制，存于项目 resources 目录）。
    /// 数据库只存相对路径与元数据，文件本体落盘 &lt;项目&gt;\resources\。
    /// </summary>
    public class Resource : BaseEntity
    {
        /// <summary>媒体类型（image/audio/video/other）</summary>
        [Required]
        [StringLength(16)]
        public string MediaType { get; set; } = default!;

        /// <summary>资源用途</summary>
        public Shared.ResourcePurposeEnum Purpose { get; set; } = Shared.ResourcePurposeEnum.Other;

        /// <summary>相对项目根目录的文件路径（如 resources\images\角色\阿乐.png）</summary>
        [Required]
        [StringLength(1024)]
        public string RelativePath { get; set; } = default!;

        /// <summary>文件名</summary>
        [StringLength(512)]
        public string? FileName { get; set; }

        /// <summary>文件大小（字节）</summary>
        public long? FileSize { get; set; }

        /// <summary>媒体时长（秒，音频/视频适用）</summary>
        public float? Duration { get; set; }

        /// <summary>宽度（图片/视频）</summary>
        public int? Width { get; set; }

        /// <summary>高度（图片/视频）</summary>
        public int? Height { get; set; }

        /// <summary>所属项目</summary>
        public long ProjectId { get; set; }
        public Project? Project { get; set; }
    }
}
