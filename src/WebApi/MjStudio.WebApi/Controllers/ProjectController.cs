using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.WebApi.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MjStudio.WebApi.Controllers
{
    /// <summary>
    /// 项目控制器（立项/加载/列表）
    /// </summary>
    [ApiController]
    [Route("api/projects")]
    public class ProjectController : ControllerBase
    {
        private readonly IProjectService _projects;

        public ProjectController(IProjectService projects) => _projects = projects;

        /// <summary>列出所有项目名</summary>
        [HttpGet]
        public async Task<IActionResult> List()
            => Ok(await _projects.ListProjectNamesAsync());

        /// <summary>获取当前项目</summary>
        [HttpGet("current")]
        public async Task<IActionResult> Current()
        {
            var name = HttpContext.RequestServices.GetRequiredService<CurrentProject>().Name;
            if (name is null) return NotFound(new { message = "未加载项目" });
            var project = await _projects.GetByNameAsync(name);
            return project is null ? NotFound() : Ok(project);
        }

        /// <summary>创建项目（立项）</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateProjectRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return BadRequest(new { message = "项目名不能为空" });

            var project = new Project
            {
                Name = req.Name,
                StoryName = req.StoryName,
                Worldview = req.Worldview,
                Description = req.Description,
                TotalEpisodes = req.TotalEpisodes,
                EpisodeDuration = req.EpisodeDuration,
                AspectRatio = req.AspectRatio,
                TargetPlatform = req.TargetPlatform,
                Genre = req.Genre,
                Audience = req.Audience,
                ArtStyle = req.ArtStyle,
                VoiceLanguage = req.VoiceLanguage,
                OriginalOrAdapted = req.OriginalOrAdapted,
                BgmStyle = req.BgmStyle,
                HasOpeningEnding = req.HasOpeningEnding,
                Deliverables = req.Deliverables,
                Status = req.Status
            };
            var created = await _projects.CreateAsync(project);
            return Ok(created);
        }

        /// <summary>加载（切换）当前项目</summary>
        [HttpPost("load/{name}")]
        public async Task<IActionResult> Load(string name)
        {
            var current = HttpContext.RequestServices.GetRequiredService<CurrentProject>();
            var project = await _projects.GetByNameAsync(name);
            if (project is null) return NotFound(new { message = $"项目 {name} 不存在" });
            current.Set(name);
            return Ok(project);
        }

        /// <summary>更新项目</summary>
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] CreateProjectRequest req)
        {
            var current = HttpContext.RequestServices.GetRequiredService<CurrentProject>();
            var project = await _projects.GetByIdAsync(id);
            if (project is null) return NotFound();
            project.StoryName = req.StoryName;
            project.Worldview = req.Worldview;
            project.Description = req.Description;
            project.TotalEpisodes = req.TotalEpisodes;
            project.EpisodeDuration = req.EpisodeDuration;
            project.AspectRatio = req.AspectRatio;
            project.TargetPlatform = req.TargetPlatform;
            project.Genre = req.Genre;
            project.Audience = req.Audience;
            project.ArtStyle = req.ArtStyle;
            project.VoiceLanguage = req.VoiceLanguage;
            project.OriginalOrAdapted = req.OriginalOrAdapted;
            project.BgmStyle = req.BgmStyle;
            project.HasOpeningEnding = req.HasOpeningEnding;
            project.Deliverables = req.Deliverables;
            project.Status = req.Status;
            await _projects.UpdateAsync(project);
            return Ok(project);
        }

        /// <summary>删除项目</summary>
        [HttpDelete("{name}")]
        public async Task<IActionResult> Delete(string name)
        {
            await _projects.DeleteAsync(name);
            return Ok(new { message = "已删除" });
        }
    }
}
