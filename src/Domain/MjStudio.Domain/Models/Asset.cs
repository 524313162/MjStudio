using System.ComponentModel.DataAnnotations;
using MjStudio.Domain.Shared;

namespace MjStudio.Domain.Models
{
    /// <summary>
    /// 资产（角色/场景/道具/BGM/音乐/音效/声线）。
    /// 所有类型统一字段：名称 + 描述 + 单个正向/反向提示词 + 资源。
    /// 类型专属信息（角色设定/外貌/场景面/区域/音频风格等）统一写入「描述」，
    /// 通过描述编写提示词；细分形态（角色声线/变装、场景子面）作为子资产表达。
    /// </summary>
    public class Asset : BaseEntity
    {
        /// <summary>
        /// 来源/归属项目ID（资产创建时所在项目）。
        /// 通用资产（IsShared=true）可被多个项目引用，引用关系见 AssetProjectRef；
        /// 项目私有资产（IsShared=false）通常只引用其来源项目。
        /// </summary>
        public long? ProjectId { get; set; }
        public Project? Project { get; set; }

        /// <summary>是否通用资产（跨项目共享，如背景/主角等）</summary>
        public bool IsShared { get; set; }

        [Required]
        public AssetTypeEnum AssetType { get; set; }

        [Required]
        [StringLength(256)]
        public string Name { get; set; } = default!;

        /// <summary>描述（用途/环境氛围/身份/外貌/场景面/区域/音频风格等全部事实信息）</summary>
        public string? Description { get; set; }

        // ============ 提示词（单个正向 + 单个反向，中英文均可） ============
        /// <summary>正向提示词（可写中文或英文）</summary>
        public string? Prompt { get; set; }

        /// <summary>反向提示词（可写中文或英文）</summary>
        public string? NegativePrompt { get; set; }

        // ============ 资源 ============
        /// <summary>主资源ID（角色立绘/场景图/道具图/BGM音频/音效文件/声线音频）</summary>
        public long? ResourceId { get; set; }
        public Resource? Resource { get; set; }

        /// <summary>排序</summary>
        public int Order { get; set; }

        // ============ 子资产（自引用，问题4） ============
        /// <summary>
        /// 父资产ID（自引用）。子资产用于表达资产的细分形态：
        /// 角色 → 声线/变装等子资产；场景 → 主要场景下的子场景/某个面。
        /// 子资产拥有与父资产相同的字段（类型/名称/描述/提示词/资源）。
        /// </summary>
        public long? ParentAssetId { get; set; }
        public Asset? ParentAsset { get; set; }

        /// <summary>子资产集合（本资产下的细分形态）</summary>
        public ICollection<Asset> Children { get; set; } = new List<Asset>();

        /// <summary>引用本资产的项目（多对多，经 AssetProjectRef）</summary>
        public ICollection<AssetProjectRef> ProjectRefs { get; set; } = new List<AssetProjectRef>();
    }
}
