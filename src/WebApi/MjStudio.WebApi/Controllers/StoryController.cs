using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.WebApi.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace MjStudio.WebApi.Controllers
{
    /// <summary>
    /// 剧本控制器（01_剧本）
    /// </summary>
    [ApiController]
    [Route("api/stories")]
    public class StoryController : ControllerBase
    {
        private readonly IStoryService _stories;
        private readonly IResourceService _resources;

        public StoryController(IStoryService stories, IResourceService resources)
        {
            _stories = stories;
            _resources = resources;
        }

        /// <summary>列出项目全部剧本</summary>
        [HttpGet]
        public async Task<IActionResult> List()
        {
            var pid = await _resources.GetProjectIdAsync();
            return Ok(await _stories.GetByProjectAsync(pid));
        }

        /// <summary>按集获取剧本</summary>
        [HttpGet("episode/{no:int}")]
        public async Task<IActionResult> GetByEpisode(int no)
        {
            var pid = await _resources.GetProjectIdAsync();
            var story = await _stories.GetByEpisodeAsync(pid, no);
            return story is null ? NotFound() : Ok(story);
        }

        /// <summary>获取单个剧本</summary>
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id)
        {
            var story = await _stories.GetByIdAsync(id);
            return story is null ? NotFound() : Ok(story);
        }

        /// <summary>创建/更新剧本</summary>
        [HttpPost]
        public async Task<IActionResult> Upsert([FromBody] StoryUpsertRequest req)
        {
            var pid = await _resources.GetProjectIdAsync();
            var story = req.Id is long id ? await _stories.GetByIdAsync(id) : null;
            if (story is null)
                story = new Story { ProjectId = pid };
            story.EpisodeNo = req.EpisodeNo;
            story.Title = req.Title;
            story.CharacterList = req.CharacterList;
            story.Content = req.Content;
            story.Duration = req.Duration;
            if (story.Id == 0)
                return Ok(await _stories.CreateAsync(story));
            await _stories.UpdateAsync(story);
            return Ok(story);
        }

        /// <summary>删除剧本</summary>
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            await _stories.DeleteAsync(id);
            return Ok(new { message = "已删除" });
        }
    }
}
