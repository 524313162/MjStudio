using MjStudio.WebApi.Dtos;
using MjStudio.WebApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MjStudio.WebApi.Controllers
{
    /// <summary>
    /// ComfyUI 工作流统一入口（所有 ComfyUI 调用走 API，不再用脚本）。
    /// 流程：POST /api/workflow 发起（返回 taskId）→ GET /api/workflow/{taskId} 轮询 → 产物自动落盘 resources。
    /// </summary>
    [ApiController]
    [Route("api/workflow")]
    public class WorkflowController : ControllerBase
    {
        private readonly WorkflowEngine _engine;

        public WorkflowController(WorkflowEngine engine) => _engine = engine;

        /// <summary>列出可用工作流模板</summary>
        [HttpGet]
        public IActionResult List()
            => Ok(_engine.ListWorkflows());

        /// <summary>读取工作流模板内容</summary>
        [HttpGet("file")]
        public IActionResult ReadFile([FromQuery] string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new WorkflowFileResponse { Success = false, Message = "缺少 name 参数" });
            try
            {
                var content = _engine.ReadWorkflow(name);
                return Ok(new WorkflowFileResponse { Success = true, Name = name, Content = content });
            }
            catch (Exception ex)
            {
                return NotFound(new WorkflowFileResponse { Success = false, Message = ex.Message });
            }
        }

        /// <summary>保存工作流模板（新建或覆盖）</summary>
        [HttpPost("file")]
        public IActionResult SaveFile([FromBody] WorkflowFileRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return BadRequest(new WorkflowFileResponse { Success = false, Message = "缺少 name 参数" });
            try
            {
                var content = _engine.SaveWorkflow(req.Name, req.Content ?? "");
                return Ok(new WorkflowFileResponse { Success = true, Name = req.Name, Content = content });
            }
            catch (Exception ex)
            {
                return BadRequest(new WorkflowFileResponse { Success = false, Message = ex.Message });
            }
        }

        /// <summary>删除工作流模板</summary>
        [HttpDelete("file")]
        public IActionResult DeleteFile([FromQuery] string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new WorkflowFileResponse { Success = false, Message = "缺少 name 参数" });
            try
            {
                _engine.DeleteWorkflow(name);
                return Ok(new WorkflowFileResponse { Success = true, Name = name, Message = "已删除" });
            }
            catch (Exception ex)
            {
                return BadRequest(new WorkflowFileResponse { Success = false, Message = ex.Message });
            }
        }

        /// <summary>直接提交完整工作流 JSON，返回 ComfyUI 原始响应</summary>
        [HttpPost("raw")]
        public async Task<IActionResult> RunRaw([FromBody] WorkflowRawRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Content))
                return BadRequest(new WorkflowRawResponse { Success = false, Message = "缺少工作流 JSON 内容" });
            try
            {
                var result = await _engine.SubmitRawAsync(req.Content);
                return Ok(new WorkflowRawResponse
                {
                    Success = true,
                    Result = result?.ToJsonString(new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    })
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new WorkflowRawResponse { Success = false, Message = ex.Message });
            }
        }

        /// <summary>发起工作流（异步，立即返回 taskId）</summary>
        [HttpPost]
        public IActionResult Run([FromBody] WorkflowRunRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Workflow))
                return BadRequest(new WorkflowResponse { Success = false, Message = "缺少 workflow 参数（工作流模板名）" });

            var task = _engine.Submit(req);
            return Ok(new WorkflowResponse
            {
                Success = true,
                TaskId = task.TaskId,
                Status = task.Status,
                Message = "已提交"
            });
        }

        /// <summary>查询任务状态/产物</summary>
        [HttpGet("{taskId}")]
        public IActionResult Get(string taskId)
        {
            var task = _engine.Get(taskId);
            if (task is null)
                return NotFound(new WorkflowResponse { Success = false, Message = $"任务 {taskId} 不存在" });

            return Ok(new WorkflowResponse
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
    }

    /// <summary>
    /// 健康检查
    /// </summary>
    [ApiController]
    public class HealthController : ControllerBase
    {
        [HttpGet("health")]
        public IActionResult Health()
            => Ok(new { status = "healthy", service = "MjStudio", time = DateTime.UtcNow });
    }
}
