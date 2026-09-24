using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MjStudio.Application.Services
{
    public class StoryService : IStoryService
    {
        private readonly IProjectDbContextFactory _factory;
        private readonly CurrentProject _current;

        public StoryService(IProjectDbContextFactory factory, CurrentProject current)
        {
            _factory = factory;
            _current = current;
        }

        public async Task<List<Story>> GetByProjectAsync(long projectId)
        {
            await using var db = _factory.Create(_current.Require());
            return await db.Stories.Where(s => s.ProjectId == projectId).OrderBy(s => s.EpisodeNo).ToListAsync();
        }

        public async Task<Story?> GetByEpisodeAsync(long projectId, int episodeNo)
        {
            await using var db = _factory.Create(_current.Require());
            return await db.Stories.Include(s => s.Chapters)
                .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.EpisodeNo == episodeNo);
        }

        public async Task<Story?> GetByIdAsync(long id)
        {
            await using var db = _factory.Create(_current.Require());
            return await db.Stories.Include(s => s.Chapters).FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<Story> CreateAsync(Story story)
        {
            await using var db = _factory.Create(_current.Require());
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            story.CreatedTime = now;
            story.UpdatedTime = now;
            db.Stories.Add(story);
            await db.SaveChangesAsync();
            return story;
        }

        public async Task UpdateAsync(Story story)
        {
            await using var db = _factory.Create(_current.Require());
            story.UpdatedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            db.Stories.Update(story);
            await db.SaveChangesAsync();
        }

        public async Task DeleteAsync(long id)
        {
            await using var db = _factory.Create(_current.Require());
            var story = await db.Stories.FirstOrDefaultAsync(s => s.Id == id);
            if (story is null) return;
            db.Stories.Remove(story);
            await db.SaveChangesAsync();
        }
    }
}
