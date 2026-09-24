using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Shared;
using MjStudio.WebApi.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace MjStudio.WebApi.Controllers
{
    /// <summary>
    /// 评审控制器（评审保留给 agent：agent 经 API 拉取环节内容做评审，结论经 API 写回）。
    /// </summary>
    [ApiController]
    [Route("api/reviews")]
    public class ReviewController : ControllerBase
    {
        private readonly IReviewService _reviews;
        private readonly IResourceService _resources;

        public ReviewController(IReviewService reviews, IResourceService resources)
        {
            _reviews = reviews;
            _resources = resources;
        }

        /// <summary>获取项目全部评审记录</summary>
        [HttpGet]
        public async Task<IActionResult> List()
        {
            var pid = await _resources.GetProjectIdAsync();
            return Ok(await _reviews.GetAllAsync(pid));
        }

        /// <summary>获取某环节评审记录</summary>
        [HttpGet("{stage}")]
        public async Task<IActionResult> GetByStage([FromRoute] StageEnum stage)
        {
            var pid = await _resources.GetProjectIdAsync();
            var review = await _reviews.GetByStageAsync(pid, stage);
            return review is null ? NotFound() : Ok(review);
        }

        /// <summary>写入/更新评审结论（agent 调用）</summary>
        [HttpPut("{stage}")]
        public async Task<IActionResult> Upsert([FromRoute] StageEnum stage, [FromBody] ReviewUpsertRequest req)
        {
            var pid = await _resources.GetProjectIdAsync();
            var existing = await _reviews.GetByStageAsync(pid, stage);
            var review = existing ?? new Domain.Models.Review { ProjectId = pid, Stage = stage };
            review.Decision = req.Decision;
            review.Content = req.Content;
            review.Issues = req.Issues;
            var result = await _reviews.UpsertAsync(review);
            return Ok(result);
        }

        /// <summary>记录一次打回（计数+1，超5次自动升级）</summary>
        [HttpPost("{stage}/revision")]
        public async Task<IActionResult> RecordRevision([FromRoute] StageEnum stage, [FromBody] ReviewUpsertRequest? req)
        {
            var pid = await _resources.GetProjectIdAsync();
            var review = await _reviews.RecordRevisionAsync(pid, stage, req?.Issues);
            return Ok(review);
        }
    }
}
