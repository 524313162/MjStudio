using MjStudio.Domain.Shared;

namespace MjStudio.WebApi.Dtos
{
    /// <summary>创建项目请求（立项字段）</summary>
    public class CreateProjectRequest
    {
        public string Name { get; set; } = "";
        public string? StoryName { get; set; }
        public string? Worldview { get; set; }
        public string? Description { get; set; }
        public int TotalEpisodes { get; set; }
        public int EpisodeDuration { get; set; } = 120;
        public AspectRatioEnum AspectRatio { get; set; } = AspectRatioEnum.Portrait916;
        public string? TargetPlatform { get; set; }
        public string? Genre { get; set; }
        public string? Audience { get; set; }
        public string? ArtStyle { get; set; }
        public string? VoiceLanguage { get; set; } = "中文普通话";
        public string? OriginalOrAdapted { get; set; } = "原创";
        public string? BgmStyle { get; set; }
        public bool HasOpeningEnding { get; set; }
        public string? Deliverables { get; set; }

        /// <summary>项目状态（进行中/已完结）</summary>
        public ProjectStatusEnum Status { get; set; } = ProjectStatusEnum.InProgress;
    }

    /// <summary>资产创建/更新请求（统一字段：名称 + 描述 + 单个正向/反向提示词）</summary>
    public class AssetUpsertRequest
    {
        public long? Id { get; set; }
        public AssetTypeEnum AssetType { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }

        // 提示词（单个正向 + 单个反向，中英文均可）
        public string? Prompt { get; set; }
        public string? NegativePrompt { get; set; }

        public int Order { get; set; }
    }

    /// <summary>资产提示词更新请求（单个正向 + 单个反向）</summary>
    public class PromptUpdateRequest
    {
        public string? Prompt { get; set; }
        public string? NegativePrompt { get; set; }
    }

    /// <summary>资产引用项目请求（多对多，全量覆盖）</summary>
    public class AssetRefsRequest
    {
        public List<long>? ProjectIds { get; set; }
    }

    /// <summary>镜头视频提示词更新请求（CN/EN）</summary>
    public class ShotPromptUpdateRequest
    {
        public string? PromptCn { get; set; }
        public string? PromptEn { get; set; }
        public string? NegativePromptEn { get; set; }
    }

    /// <summary>镜头资产引用项（参考图排定：1=场景组合图，2~N=角色/场景/道具）</summary>
    public class ShotAssetRefItem
    {
        public long AssetId { get; set; }
        public int MediaIndex { get; set; }
        public string RefType { get; set; } = "";
    }

    /// <summary>设置镜头资产引用请求（全量覆盖）</summary>
    public class ShotAssetRefsRequest
    {
        public List<ShotAssetRefItem> Refs { get; set; } = new();
    }

    /// <summary>剧本创建/更新请求</summary>
    public class StoryUpsertRequest
    {
        public long? Id { get; set; }
        public int EpisodeNo { get; set; }
        public string Title { get; set; } = "";
        public string? CharacterList { get; set; }
        public string? Content { get; set; }
        public int? Duration { get; set; }
    }

    /// <summary>集创建/更新请求</summary>
    public class EpisodeUpsertRequest
    {
        public long? Id { get; set; }
        public int EpisodeNo { get; set; }
        public string Name { get; set; } = "";
        public int? Duration { get; set; }
        public string? AssetWhitelist { get; set; }
        public string? CameraSwitchTable { get; set; }
        public string? AudioCueTable { get; set; }
        public string? DialogueList { get; set; }
    }

    /// <summary>镜头创建/更新请求（11 项字段 + CN/EN 视频提示词）</summary>
    public class ShotUpsertRequest
    {
        public long? Id { get; set; }
        public long EpisodeId { get; set; }
        public int ShotNo { get; set; }
        public string Title { get; set; } = "";
        public string? VideoContent { get; set; }
        public string? FirstFrameDesc { get; set; }
        public string? Camera { get; set; }
        public string? ShotSize { get; set; }
        public string? CameraFacing { get; set; }
        public string? SpatialPosition { get; set; }
        public string? Bgm { get; set; }
        public string? BgmRange { get; set; }
        public string? Timecode { get; set; }
        public string? Timeline { get; set; }
        public string? VoiceConstraint { get; set; }
        public string? AmbientSfx { get; set; }
        public string? SfxRange { get; set; }
        public float? Duration { get; set; }
        public string? Transition { get; set; }
        public string? VideoPromptCn { get; set; }
        public string? VideoPromptEn { get; set; }
        public string? VideoNegativePromptEn { get; set; }
    }

    /// <summary>评审写入请求（agent 调用）</summary>
    public class ReviewUpsertRequest
    {
        public ReviewDecisionEnum Decision { get; set; }
        public string? Content { get; set; }
        public string? Issues { get; set; }
    }

    /// <summary>工作流响应</summary>
    public class WorkflowResponse
    {
        public bool Success { get; set; }
        public string? TaskId { get; set; }
        public string? Status { get; set; }
        public string? Message { get; set; }
        public string? MediaType { get; set; }
        public long? ResourceId { get; set; }
        public string? RelativePath { get; set; }
    }

    /// <summary>工作流文件操作请求（读取/保存/删除）</summary>
    public class WorkflowFileRequest
    {
        /// <summary>工作流模板名（不含 .json）</summary>
        public string Name { get; set; } = "";

        /// <summary>工作流 JSON 内容（保存时必填）</summary>
        public string? Content { get; set; }
    }

    /// <summary>工作流文件响应</summary>
    public class WorkflowFileResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Name { get; set; }
        public string? Content { get; set; }
    }

    /// <summary>直接提交完整工作流 JSON 的请求</summary>
    public class WorkflowRawRequest
    {
        /// <summary>完整工作流 JSON 内容</summary>
        public string Content { get; set; } = "";
    }

    /// <summary>直接提交的响应（返回 ComfyUI 原始 JSON）</summary>
    public class WorkflowRawResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Result { get; set; }
    }
}
