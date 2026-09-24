using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Shared
{
    /// <summary>
    /// 画幅比例
    /// </summary>
    public enum AspectRatioEnum
    {
        [Display(Name = "竖屏 9:16")]
        Portrait916 = 1,

        [Display(Name = "横屏 16:9")]
        Landscape169 = 2,

        [Display(Name = "方形 1:1")]
        Square11 = 3
    }
}
