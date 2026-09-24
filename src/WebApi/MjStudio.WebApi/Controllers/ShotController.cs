using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.WebApi.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace MjStudio.WebApi.Controllers
{
    /// <summary>
    /// 分镜控制器（03_分镜：集 + 镜头 + 资产引用）
    /// </summary>
    [ApiController]
    [Route("api")]
    public class ShotController : ControllerBase
    {
        private readonly IShotService _shots;
        private readonly IResourceService _resources;

        public ShotController(IShotService shots, IResourceService resources)
        {
            _shots = shots;
            _resources = resources;
        }

        // ===== 集 =====
        [HttpGet("episodes")]
        public async Task<IActionResult> ListEpisodes()
        {
            var pid = await _resources.GetProjectIdAsync();
            return Ok(await _shots.GetEpisodesAsync(pid));
        }

        [HttpGet("episodes/{no:int}")]
        public async Task<IActionResult> GetEpisode(int no)
        {
            var pid = await _resources.GetProjectIdAsync();
            var ep = await _shots.GetEpisodeAsync(pid, no);
            return ep is null ? NotFound() : Ok(ep);
        }

        [HttpPost("episodes")]
        public async Task<IActionResult> UpsertEpisode([FromBody] EpisodeUpsertRequest req)
        {
            var pid = await _resources.GetProjectIdAsync();
            Episode? ep = null;
            if (req.Id is long id)
            {
                var all = await _shots.GetEpisodesAsync(pid);
                ep = all.FirstOrDefault(e => e.Id == id);
            }
            if (ep is null)
                ep = new Episode { ProjectId = pid };
            ep.EpisodeNo = req.EpisodeNo;
            ep.Name = req.Name;
            ep.Duration = req.Duration;
            ep.AssetWhitelist = req.AssetWhitelist;
            ep.CameraSwitchTable = req.CameraSwitchTable;
            ep.AudioCueTable = req.AudioCueTable;
            ep.DialogueList = req.DialogueList;
            if (ep.Id == 0)
                return Ok(await _shots.CreateEpisodeAsync(ep));
            await _shots.UpdateEpisodeAsync(ep);
            return Ok(ep);
        }

        // ===== 镜头 =====
        [HttpGet("episodes/{episodeId:long}/shots")]
        public async Task<IActionResult> ListShots(long episodeId)
            => Ok(await _shots.GetShotsAsync(episodeId));

        [HttpGet("shots/{id:long}")]
        public async Task<IActionResult> GetShot(long id)
        {
            var shot = await _shots.GetShotAsync(id);
            return shot is null ? NotFound() : Ok(shot);
        }

        [HttpPost("shots")]
        public async Task<IActionResult> UpsertShot([FromBody] ShotUpsertRequest req)
        {
            var shot = req.Id is long id ? await _shots.GetShotAsync(id) : null;
            if (shot is null)
                shot = new Shot { EpisodeId = req.EpisodeId };
            shot.ShotNo = req.ShotNo;
            shot.Title = req.Title;
            shot.VideoContent = req.VideoContent;
            shot.FirstFrameDesc = req.FirstFrameDesc;
            shot.Camera = req.Camera;
            shot.ShotSize = req.ShotSize;
            shot.CameraFacing = req.CameraFacing;
            shot.SpatialPosition = req.SpatialPosition;
            shot.Bgm = req.Bgm;
            shot.BgmRange = req.BgmRange;
            shot.Timecode = req.Timecode;
            shot.Timeline = req.Timeline;
            shot.VoiceConstraint = req.VoiceConstraint;
            shot.AmbientSfx = req.AmbientSfx;
            shot.SfxRange = req.SfxRange;
            shot.Duration = req.Duration;
            shot.Transition = req.Transition;
            shot.VideoPromptCn = req.VideoPromptCn;
            shot.VideoPromptEn = req.VideoPromptEn;
            shot.VideoNegativePromptEn = req.VideoNegativePromptEn;
            if (shot.Id == 0)
                return Ok(await _shots.CreateShotAsync(shot));
            await _shots.UpdateShotAsync(shot);
            return Ok(shot);
        }

        /// <summary>更新镜头视频提示词（CN/EN）</summary>
        [HttpPut("shots/{id:long}/prompts")]
        public async Task<IActionResult> UpdateShotPrompts(long id, [FromBody] ShotPromptUpdateRequest req)
        {
            await _shots.UpdateVideoPromptsAsync(id, req.PromptCn, req.PromptEn, req.NegativePromptEn);
            return Ok(await _shots.GetShotAsync(id));
        }

        [HttpDelete("shots/{id:long}")]
        public async Task<IActionResult> DeleteShot(long id)
        {
            await _shots.DeleteShotAsync(id);
            return Ok(new { message = "已删除" });
        }
    }
}
