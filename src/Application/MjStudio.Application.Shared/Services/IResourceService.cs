using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;

namespace MjStudio.Application.Shared.Services
{
    /// <summary>
    /// 资源服务契约（二进制落盘 resources + 元数据入库）
    /// </summary>
    public interface IResourceService
    {
        /// <summary>保存资源文件并创建记录</summary>
        Task<Resource> SaveAsync(long projectId, byte[] bytes, string subDir, string fileName,
            string mediaType, ResourcePurposeEnum purpose, float? duration = null);

        /// <summary>读取资源字节（不存在返回 null）</summary>
        Task<byte[]?> ReadAsync(long resourceId);

        /// <summary>获取资源绝对路径（不存在返回 null）</summary>
        Task<string?> GetAbsolutePathAsync(long resourceId);

        /// <summary>按项目列出资源</summary>
        Task<List<Resource>> GetByProjectAsync(long projectId);

        /// <summary>删除资源（文件 + 记录）</summary>
        Task DeleteAsync(long resourceId);

        /// <summary>获取当前项目ID</summary>
        Task<long> GetProjectIdAsync();
    }
}
