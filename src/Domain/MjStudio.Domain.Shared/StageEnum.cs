using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Shared
{
    /// <summary>
    /// 制作环节（对应技能全流程步骤，用于评审与流程状态）
    /// </summary>
    public enum StageEnum
    {
        [Display(Name = "立项")]
        Init = 0,

        [Display(Name = "剧本")]
        Script = 1,

        [Display(Name = "分镜")]
        Storyboard = 2,

        [Display(Name = "资产")]
        Asset = 3,

        [Display(Name = "声线")]
        Voice = 4,

        [Display(Name = "音乐")]
        Music = 5,

        [Display(Name = "音效")]
        SoundEffect = 6,

        [Display(Name = "视频")]
        Video = 7,

        [Display(Name = "配音")]
        Dubbing = 8,

        [Display(Name = "合成")]
        Compose = 9
    }
}
