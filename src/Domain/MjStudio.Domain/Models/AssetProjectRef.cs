namespace MjStudio.Domain.Models
{
    /// <summary>
    /// 资产-项目引用（多对多）：记录某个资产被哪些项目使用。
    /// 通用资产（Asset.IsShared=true）通常被多个项目引用；
    /// 项目私有资产通常只引用其来源项目。
    /// </summary>
    public class AssetProjectRef : BaseEntity
    {
        public long AssetId { get; set; }
        public Asset? Asset { get; set; }

        public long ProjectId { get; set; }
        public Project? Project { get; set; }
    }
}
