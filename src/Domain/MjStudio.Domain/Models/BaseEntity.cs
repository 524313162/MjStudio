using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Models
{
    /// <summary>
    /// 基础实体基类
    /// </summary>
    public abstract class BaseEntity
    {
        [Key]
        public long Id { get; set; }

        /// <summary>创建时间（Unix 毫秒）</summary>
        public long CreatedTime { get; set; }

        /// <summary>更新时间（Unix 毫秒）</summary>
        public long UpdatedTime { get; set; }
    }
}
