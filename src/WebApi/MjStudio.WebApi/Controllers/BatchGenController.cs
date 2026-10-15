using MjStudio.WebApi.Dtos;
using MjStudio.WebApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MjStudio.WebApi.Controllers
{
    /// <summary>
    /// 批量生成接口（分模块：image / audio / video）。
    /// 依赖当前项目上下文（先 POST /api/projects/load/{name}）。
    /// </summary>
    [ApiController]
    [Route("api/batchgen")]
    public class BatchGenController : ControllerBase
    {
        private readonly BatchGenService _svc;

        public BatchGenController(BatchGenService svc) => _svc = svc;

        /// <summary>列出某模块下全部资产/分镜（含是否已有内容）</summary>
        /// <param name="module">image / audio / video</param>
        [HttpGet("items")]
        public async Task<IActionResult> GetItems([FromQuery] string module)
        {
            try
            {
                return Ok(await _svc.GetItemsAsync(module));
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>全部生成（自动跳过已存在内容，仅对未生成项入队）</summary>
        [HttpPost("generate-all")]
        public async Task<IActionResult> GenerateAll([FromBody] BatchGenModuleRequest req)
        {
            try
            {
                var items = await _svc.GetItemsAsync(req.Module);
                var pending = items.Where(i => !i.HasContent).ToList();
                var results = new List<BatchGenSubmitResult>();
                foreach (var it in pending)
                {
                    try
                    {
                        var task = _svc.SubmitForItem(it);
                        results.Add(new BatchGenSubmitResult
                        {
                            Id = it.Id,
                            Name = it.Name,
                            Success = true,
                            TaskId = task.TaskId
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new BatchGenSubmitResult
                        {
                            Id = it.Id,
                            Name = it.Name,
                            Success = false,
                            Message = ex.Message
                        });
                    }
                }
                return Ok(new BatchGenAllResponse
                {
                    Total = items.Count,
                    Pending = pending.Count,
                    Results = results
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>单项生成（针对某个具体资产/分镜）</summary>
        [HttpPost("generate")]
        public IActionResult GenerateSingle([FromBody] BatchGenItem item)
        {
            try
            {
                var task = _svc.SubmitForItem(item);
                return Ok(new BatchGenSubmitResult
                {
                    Id = item.Id,
                    Name = item.Name,
                    Success = true,
                    TaskId = task.TaskId
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new BatchGenSubmitResult
                {
                    Id = item.Id,
                    Name = item.Name,
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        /// <summary>清理单项内容（删文件 + 重置为未生成状态）</summary>
        [HttpPost("clear")]
        public async Task<IActionResult> Clear([FromBody] BatchGenItem item)
        {
            try
            {
                await _svc.ClearAsync(item);
                return Ok(new { message = "已清理" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
