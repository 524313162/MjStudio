using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using MjStudio.WebApi.Dtos;

namespace MjStudio.Host.ViewModels
{
    /// <summary>
    /// 批量生成弹窗中的资产/分镜卡片项
    /// </summary>
    public class BatchGenCardItem : ViewModelBase
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string TypeDisplay { get; set; } = "";
        public string? Description { get; set; }

        /// <summary>模块类型：image / audio / video</summary>
        public string ModuleType { get; set; } = "";

        public AssetTypeEnum? AssetType { get; set; }
        public int? ShotNo { get; set; }
        public int? EpisodeNo { get; set; }

        // 生成参数
        public string? Prompt { get; set; }
        public string? NegativePrompt { get; set; }
        public float? Duration { get; set; }
        public List<string> ReferenceImagePaths { get; set; } = new();

        /// <summary>参考图绝对路径列表（视频模块用，展示分镜绑定的资产缩略图）</summary>
        public List<string> ReferenceImages { get; set; } = new();
        public bool HasReferenceImages => ReferenceImages.Count > 0;

        /// <summary>缩略图绝对路径（图片模块用）</summary>
        private string? _imagePath;
        public string? ImagePath
        {
            get => _imagePath;
            set
            {
                if (SetProperty(ref _imagePath, value))
                    OnPropertyChanged(nameof(HasImage));
            }
        }
        public bool HasImage => !string.IsNullOrEmpty(ImagePath) && System.IO.File.Exists(ImagePath);

        /// <summary>模块图标</summary>
        public string ModuleIcon => ModuleType switch
        {
            "image" => "🖼",
            "audio" => "🎵",
            "video" => "🎬",
            _ => "📦"
        };

        // ===== 状态 =====
        private bool _hasContent;
        public bool HasContent
        {
            get => _hasContent;
            set
            {
                if (SetProperty(ref _hasContent, value))
                    OnPropertyChanged(nameof(ActionButtonText));
            }
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (SetProperty(ref _isGenerating, value))
                {
                    OnPropertyChanged(nameof(ActionButtonText));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => IsGenerating ? "⏳ 生成中…" : (HasContent ? "✓ 已生成" : _statusText);
            set => SetProperty(ref _statusText, value);
        }

        public string ActionButtonText => IsGenerating ? "生成中…" : (HasContent ? "🗑 删除" : "⚡ 去生成");

        // ===== 工厂方法 =====

        public static BatchGenCardItem FromAsset(Asset asset, string moduleType)
        {
            return new BatchGenCardItem
            {
                Id = asset.Id,
                Name = asset.Name ?? "",
                TypeDisplay = asset.AssetType switch
                {
                    AssetTypeEnum.Character => "角色",
                    AssetTypeEnum.Scene => "场景",
                    AssetTypeEnum.Prop => "道具",
                    AssetTypeEnum.Bgm => "BGM",
                    AssetTypeEnum.Music => "音乐",
                    AssetTypeEnum.SoundEffect => "音效",
                    AssetTypeEnum.Voice => "声线",
                    _ => asset.AssetType.ToString()
                },
                Description = asset.Description,
                ModuleType = moduleType,
                AssetType = asset.AssetType,
                Prompt = asset.Prompt,
                NegativePrompt = asset.NegativePrompt,
                HasContent = asset.ResourceId is not null && asset.Resource is not null
            };
        }

        public static BatchGenCardItem FromShot(Shot shot, int episodeNo, List<string> refImagePaths)
        {
            return new BatchGenCardItem
            {
                Id = shot.Id,
                Name = shot.Title ?? "",
                TypeDisplay = $"镜头#{shot.ShotNo}",
                Description = shot.VideoContent,
                ModuleType = "video",
                ShotNo = shot.ShotNo,
                EpisodeNo = episodeNo,
                Prompt = shot.VideoPromptCn ?? shot.VideoContent,
                NegativePrompt = shot.VideoNegativePromptEn,
                Duration = shot.Duration,
                ReferenceImagePaths = refImagePaths,
                HasContent = shot.VideoResourceId is not null
            };
        }

        public static BatchGenCardItem From(BatchGenItem it)
        {
            return new BatchGenCardItem
            {
                Id = it.Id,
                Name = it.Name,
                TypeDisplay = it.TypeDisplay,
                Description = it.Description,
                ModuleType = it.ModuleType,
                AssetType = it.AssetType,
                ShotNo = it.ShotNo,
                EpisodeNo = it.EpisodeNo,
                Prompt = it.Prompt,
                NegativePrompt = it.NegativePrompt,
                Duration = it.Duration,
                ReferenceImagePaths = it.ReferenceImagePaths,
                HasContent = it.HasContent
            };
        }
    }
}
