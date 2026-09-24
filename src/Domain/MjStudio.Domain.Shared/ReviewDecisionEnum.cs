using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Shared
{
    /// <summary>
    /// 评审结论
    /// </summary>
    public enum ReviewDecisionEnum
    {
        [Display(Name = "待评审")]
        Pending = 0,

        [Display(Name = "通过")]
        Approved = 1,

        [Display(Name = "打回")]
        Revision = 2,

        [Display(Name = "升级")]
        Escalated = 3
    }
}
