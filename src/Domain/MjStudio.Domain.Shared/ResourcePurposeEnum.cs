using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Shared
{
    /// <summary>
    /// 资源用途（资源文件在流程中的角色）
    /// </summary>
    public enum ResourcePurposeEnum
    {
        [Display(Name = "资产主图")]
        AssetImage = 0,

        [Display(Name = "资产音频")]
        AssetAudio = 1,

        [Display(Name = "声线种子")]
        VoiceSeed = 2,

        [Display(Name = "镜头视频")]
        ShotVideo = 3,

        [Display(Name = "放大视频")]
        UpscaledVideo = 4,

        [Display(Name = "配音片段")]
        DubbingClip = 5,

        [Display(Name = "成片")]
        FinalVideo = 6,

        [Display(Name = "封面")]
        Cover = 7,

        [Display(Name = "其它")]
        Other = 99
    }
}
