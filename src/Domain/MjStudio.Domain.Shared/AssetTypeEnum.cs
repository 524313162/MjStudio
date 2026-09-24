using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Shared
{
    /// <summary>
    /// 资产类型（对应技能 02_资产 下的分类）
    /// </summary>
    public enum AssetTypeEnum
    {
        [Display(Name = "角色")]
        Character = 1,

        [Display(Name = "场景")]
        Scene = 2,

        [Display(Name = "道具")]
        Prop = 3,

        [Display(Name = "BGM")]
        Bgm = 4,

        [Display(Name = "音乐")]
        Music = 5,

        [Display(Name = "音效")]
        SoundEffect = 6,

        [Display(Name = "声线")]
        Voice = 7
    }
}
