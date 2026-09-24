using MjStudio.Domain.Models;

namespace MjStudio.Application.Shared.Services
{
    /// <summary>
    /// 项目服务契约
    /// </summary>
    public interface IProjectService
    {
        /// <summary>列出根目录下所有项目（含未初始化的文件夹）</summary>
        Task<List<string>> ListProjectNamesAsync();

        /// <summary>获取项目（按名）</summary>
        Task<Project?> GetByNameAsync(string name);

        /// <summary>获取项目（按ID）</summary>
        Task<Project?> GetByIdAsync(long id);

        /// <summary>创建项目（初始化文件夹 + 数据库 + 立项记录）</summary>
        Task<Project> CreateAsync(Project project);

        /// <summary>更新项目</summary>
        Task UpdateAsync(Project project);

        /// <summary>删除项目（含文件夹）</summary>
        Task DeleteAsync(string name);
    }
}
