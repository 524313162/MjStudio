using System.ComponentModel.DataAnnotations;

namespace MjStudio.Domain.Models
{
    /// <summary>
    /// 分镜镜头（对应 03_分镜\第N集镜头.md 的一个镜头，含 11 项必填字段）。
    /// 一镜一视频：一个镜头 = 一个视频 = 一个机位。
    /// </summary>
    public class Shot : BaseEntity
    {
        public long EpisodeId { get; set; }
        public Episode? Episode { get; set; }

        /// <summary>镜头号（第N集内序号）</summary>
        public int ShotNo { get; set; }

        [Required]
        [StringLength(256)]
        public string Title { get; set; } = default!;

        /// <summary>1. 视频内容（谁在哪做什么，情绪/光线）</summary>
        public string? VideoContent { get; set; }

        /// <summary>2. 首帧画面描述（第一帧定格：人物位置/姿势表情/景别构图/光线）</summary>
        public string? FirstFrameDesc { get; set; }

        /// <summary>3. 运镜（景别+机位+运镜三要素，机位须写明镜头方位/朝向）</summary>
        public string? Camera { get; set; }

        /// <summary>景别（全景/中景/近景/特写）</summary>
        [StringLength(32)]
        public string? ShotSize { get; set; }

        /// <summary>机位朝向（镜头朝东/朝南等）</summary>
        [StringLength(64)]
        public string? CameraFacing { get; set; }

        /// <summary>4. 空间方位（标志物位置/人物站位/运动方向/人物面朝方向）</summary>
        public string? SpatialPosition { get; set; }

        /// <summary>6. BGM（本镜用哪首，或"无"）</summary>
        [StringLength(256)]
        public string? Bgm { get; set; }

        /// <summary>BGM 播放时间段（如 0-5s）</summary>
        [StringLength(64)]
        public string? BgmRange { get; set; }

        /// <summary>7. 全局时间码 TC（00:MM.SS~00:MM.SS）</summary>
        [StringLength(64)]
        public string? Timecode { get; set; }

        /// <summary>镜头时间轴（全阶段动作分段，Markdown）</summary>
        public string? Timeline { get; set; }

        /// <summary>8. 发声约束</summary>
        public string? VoiceConstraint { get; set; }

        /// <summary>9. 环境音效（随画面生成的一次性随机环境音描述）</summary>
        public string? AmbientSfx { get; set; }

        /// <summary>环境音效播放时间段（如 0-5s）</summary>
        [StringLength(64)]
        public string? SfxRange { get; set; }

        /// <summary>10. 时长（秒，5~10）</summary>
        public float? Duration { get; set; }

        /// <summary>11. 转场（硬切/叠化/淡入淡出/闪白/摇移承接）</summary>
        [StringLength(64)]
        public string? Transition { get; set; }

        // ============ 视频提示词（CN/EN 双字段，需求 6/7） ============
        /// <summary>视频提示词（中文，供审核）</summary>
        public string? VideoPromptCn { get; set; }

        /// <summary>视频提示词（英文，@xx 模块式，提交用）</summary>
        public string? VideoPromptEn { get; set; }

        /// <summary>视频反向提示词（英文）</summary>
        public string? VideoNegativePromptEn { get; set; }

        // ============ 资源 ============
        /// <summary>镜头视频资源ID（videoX.mp4）</summary>
        public long? VideoResourceId { get; set; }
        public Resource? VideoResource { get; set; }

        /// <summary>放大视频资源ID（videoX_1K.mp4）</summary>
        public long? UpscaledVideoResourceId { get; set; }
        public Resource? UpscaledVideoResource { get; set; }

        /// <summary>配音视频资源ID（视频X_配音.mp4）</summary>
        public long? DubbedVideoResourceId { get; set; }
        public Resource? DubbedVideoResource { get; set; }

        /// <summary>参考图数量 N（决定 15.MinimaxH3 多图参考{N:02d} 档位）</summary>
        public int RefImageCount { get; set; }

        public ICollection<ShotAssetRef> AssetRefs { get; set; } = new List<ShotAssetRef>();
    }
}
