using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Shared
{
    /// <summary>
    /// 项目状态：进行中 / 已完结。
    /// 已完结的项目不能再被资产绑定（资产引用项目时过滤掉已完结项目）。
    /// </summary>
    public enum ProjectStatusEnum
    {
        [Display(Name = "进行中")]
        InProgress = 0,

        [Display(Name = "已完结")]
        Completed = 1
    }
}
