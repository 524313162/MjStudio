using MjStudio.WebApi.Dtos;
using MjStudio.WebApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MjStudio.WebApi.Controllers
{
    /// <summary>
    /// 生成接口（9 个业务接口，封装 ComfyUI 工作流 / ffmpeg）。
    /// 每个接口两个端点：
    ///   POST /api/gen/{type}       提交任务（立即返回 taskId）
    ///   GET  /api/gen/{type}/{taskId}  查询状态（阻塞等待最多 60s，超时返回 running）
    /// </summary>
    [ApiController]
    [Route("api/gen")]
    public class GenerationController : ControllerBase
    {
        private readonly GenerationEngine _engine;

        public GenerationController(GenerationEngine engine) => _engine = engine;

        // ==================== 1. 角色生成 ====================

        /// <summary>提交角色生成（ZImage 文生图）</summary>
        [HttpPost("character")]
        public IActionResult SubmitCharacter([FromBody] ImageGenRequest req)
            => SubmitTask("character", req);

        /// <summary>查询角色生成状态</summary>
        [HttpGet("character/{taskId}")]
        public async Task<IActionResult> PollCharacter(string taskId)
            => await PollTask(taskId);

        // ==================== 2. 场景生成 ====================

        /// <summary>提交场景生成（ZImage 文生图）</summary>
        [HttpPost("scene")]
        public IActionResult SubmitScene([FromBody] ImageGenRequest req)
            => SubmitTask("scene", req);

        /// <summary>查询场景生成状态</summary>
        [HttpGet("scene/{taskId}")]
        public async Task<IActionResult> PollScene(string taskId)
            => await PollTask(taskId);

        // ==================== 3. 道具生成 ====================

        /// <summary>提交道具生成（ZImage 文生图）</summary>
        [HttpPost("prop")]
        public IActionResult SubmitProp([FromBody] ImageGenRequest req)
            => SubmitTask("prop", req);

        /// <summary>查询道具生成状态</summary>
        [HttpGet("prop/{taskId}")]
        public async Task<IActionResult> PollProp(string taskId)
            => await PollTask(taskId);

        // ==================== 4. BGM 生成 ====================

        /// <summary>提交 BGM 生成（STABLE-BGM）</summary>
        [HttpPost("bgm")]
        public IActionResult SubmitBgm([FromBody] AudioGenRequest req)
            => SubmitTask("bgm", req);

        /// <summary>查询 BGM 生成状态</summary>
        [HttpGet("bgm/{taskId}")]
        public async Task<IActionResult> PollBgm(string taskId)
            => await PollTask(taskId);

        // ==================== 5. 音效生成 ====================

        /// <summary>提交音效生成（STABLE-BGM）</summary>
        [HttpPost("sfx")]
        public IActionResult SubmitSfx([FromBody] AudioGenRequest req)
            => SubmitTask("sfx", req);

        /// <summary>查询音效生成状态</summary>
        [HttpGet("sfx/{taskId}")]
        public async Task<IActionResult> PollSfx(string taskId)
            => await PollTask(taskId);

        // ==================== 6. 图片编辑 ====================

        /// <summary>提交图片编辑（FLUX2）</summary>
        [HttpPost("image-edit")]
        public IActionResult SubmitImageEdit([FromBody] ImageEditRequest req)
            => SubmitTask("imageEdit", req);

        /// <summary>查询图片编辑状态</summary>
        [HttpGet("image-edit/{taskId}")]
        public async Task<IActionResult> PollImageEdit(string taskId)
            => await PollTask(taskId);

        // ==================== 7. 视频生成 ====================

        /// <summary>提交视频生成（MinimaxH3 多图参考，1~20 张）</summary>
        [HttpPost("video")]
        public IActionResult SubmitVideo([FromBody] VideoGenRequest req)
            => SubmitTask("video", req);

        /// <summary>查询视频生成状态</summary>
        [HttpGet("video/{taskId}")]
        public async Task<IActionResult> PollVideo(string taskId)
            => await PollTask(taskId);

        // ==================== 8. 视频合成 ====================

        /// <summary>提交视频合成（ffmpeg concat 拼接）</summary>
        [HttpPost("video-compose")]
        public IActionResult SubmitVideoCompose([FromBody] VideoComposeRequest req)
            => SubmitTask("videoCompose", req);

        /// <summary>查询视频合成状态</summary>
        [HttpGet("video-compose/{taskId}")]
        public async Task<IActionResult> PollVideoCompose(string taskId)
            => await PollTask(taskId);

        // ==================== 9. 视频导出 ====================

        /// <summary>提交视频导出（声音转换 + 拆分 + 合成）</summary>
        [HttpPost("video-export")]
        public IActionResult SubmitVideoExport([FromBody] VideoExportRequest req)
            => SubmitTask("videoExport", req);

        /// <summary>查询视频导出状态</summary>
        [HttpGet("video-export/{taskId}")]
        public async Task<IActionResult> PollVideoExport(string taskId)
            => await PollTask(taskId);

        // ==================== 通用方法 ====================

        private IActionResult SubmitTask(string type, object request)
        {
            try
            {
                var task = _engine.Submit(type, request);
                return Ok(new GenerationResponse
                {
                    Success = true,
                    TaskId = task.TaskId,
                    Status = task.Status,
                    Message = "已提交"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new GenerationResponse
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        private async Task<IActionResult> PollTask(string taskId)
        {
            try
            {
                var task = await _engine.PollAsync(taskId, timeoutSec: 60);
                return Ok(new GenerationResponse
                {
                    Success = task.Status != "failed",
                    TaskId = task.TaskId,
                    Status = task.Status,
                    Message = task.Message,
                    MediaType = task.MediaType,
                    ResourceId = task.ResourceId,
                    RelativePath = task.RelativePath
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new GenerationResponse
                {
                    Success = false,
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new GenerationResponse
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }
    }
}
