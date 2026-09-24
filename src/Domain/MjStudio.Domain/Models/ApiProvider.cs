using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Models
{
    /// <summary>
    /// API 提供方（ComfyUI 等外部服务连接配置）
    /// </summary>
    public class ApiProvider : BaseEntity
    {
        [Required]
        [StringLength(256)]
        public string Name { get; set; } = default!;

        /// <summary>服务地址</summary>
        [StringLength(512)]
        public string? ApiUrl { get; set; }

        /// <summary>API Key（可选）</summary>
        [StringLength(1024)]
        public string? ApiKey { get; set; }

        /// <summary>能力（image/audio/video）</summary>
        [StringLength(64)]
        public string? Capability { get; set; }

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;
    }
}
