using System.ComponentModel.DataAnnotations;
using MjStudio.Domain.Shared;

namespace MjStudio.Domain.Models
{
    /// <summary>
    /// 评审记录（对应 00_项目\评审\*.md，每个环节一条）。
    /// 评审保留给 agent：agent 通过 API 拉取环节内容做评审，结论经 API 写回本表。
    /// 打回规则：同一环节最多打回 5 次，第 6 次升级用户。
    /// </summary>
    public class Review : BaseEntity
    {
        public long ProjectId { get; set; }
        public Project? Project { get; set; }

        /// <summary>评审环节</summary>
        [Required]
        public StageEnum Stage { get; set; }

        /// <summary>评审结论</summary>
        public ReviewDecisionEnum Decision { get; set; } = ReviewDecisionEnum.Pending;

        /// <summary>评审正文（Markdown，逐项核对记录）</summary>
        public string? Content { get; set; }

        /// <summary>问题清单（编号问题列表，Markdown）</summary>
        public string? Issues { get; set; }

        /// <summary>已打回次数</summary>
        public int RevisionCount { get; set; }

        /// <summary>评审时间（Unix 毫秒）</summary>
        public long ReviewedTime { get; set; }
    }
}
