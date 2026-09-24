using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Models
{
    /// <summary>
    /// 镜头-资产引用（分镜每镜用到的资产引用，含参考图排定顺序）。
    /// media_1 恒为本镜状态+朝向匹配的场景组合图，media_2~N 依次为角色→场景→道具。
    /// </summary>
    public class ShotAssetRef : BaseEntity
    {
        public long ShotId { get; set; }
        public Shot? Shot { get; set; }

        public long AssetId { get; set; }
        public Asset? Asset { get; set; }

        /// <summary>参考图序号（1=场景组合图，2~N=角色/场景/道具）</summary>
        public int MediaIndex { get; set; }

        /// <summary>引用类型（scene/character/prop）</summary>
        [StringLength(32)]
        public string? RefType { get; set; }
    }
}
