using MjStudio.Domain.Models;

namespace MjStudio.Application.Shared.Services
{
    /// <summary>
    /// 剧本服务契约
    /// </summary>
    public interface IStoryService
    {
        Task<List<Story>> GetByProjectAsync(long projectId);
        Task<Story?> GetByEpisodeAsync(long projectId, int episodeNo);
        Task<Story?> GetByIdAsync(long id);
        Task<Story> CreateAsync(Story story);
        Task UpdateAsync(Story story);
        Task DeleteAsync(long id);
    }
}
