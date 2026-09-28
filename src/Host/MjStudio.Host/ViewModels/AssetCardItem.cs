using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Reflection;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using MjStudio.Infrastructure;

namespace MjStudio.Host.ViewModels
{
    /// <summary>
    /// 资产表格行项：包装 Asset 供表格 UI 绑定。
    /// 负责解析资源绝对路径（图片/音频）、组合全部描述（提示词）文本、构建子资产行。
    /// </summary>
    public class AssetCardItem : ViewModelBase
    {
        private readonly ResourceStorageService _resources;
        private readonly IReadOnlyList<string> _projectNames;

        public Asset Asset { get; }

        public AssetCardItem(Asset asset, ResourceStorageService resources, IReadOnlyList<string> projectNames)
        {
            Asset = asset;
            _resources = resources;
            _projectNames = projectNames;

            TypeDisplay = GetDisplayName(asset.AssetType);
            RefProjects = BuildRefProjects(asset);
            FullDescription = BuildFullDescription(asset);

            // 音频资产（BGM/音乐/音效/声线）→ 音频路径；图片资产 → 图片路径
            var isAudio = asset.AssetType is AssetTypeEnum.Bgm or AssetTypeEnum.Music or AssetTypeEnum.SoundEffect or AssetTypeEnum.Voice;
            var resolved = ResolveResource(asset.Resource);
            if (isAudio) AudioPath = resolved;
            else ImagePath = resolved;

            // 子资产行
            if (asset.Children is { Count: > 0 })
            {
                foreach (var c in asset.Children)
                    Children.Add(new AssetCardItem(c, resources, projectNames));
            }
        }

        public string Name => Asset.Name ?? "";
        public string TypeDisplay { get; }
        public string Description => Asset.Description ?? "";

        /// <summary>正向提示词（单个）</summary>
        public string Prompt => Asset.Prompt ?? "";

        /// <summary>反向提示词（单个）</summary>
        public string NegativePrompt => Asset.NegativePrompt ?? "";

        /// <summary>引用项目名（绑定项目即可用；未绑定则不可用）</summary>
        public string RefProjects { get; }

        /// <summary>图片绝对路径（无则 null）</summary>
        public string? ImagePath { get; }

        public bool HasImage => !string.IsNullOrEmpty(ImagePath) && File.Exists(ImagePath);

        /// <summary>音频绝对路径（无则 null）</summary>
        public string? AudioPath { get; }

        public bool HasAudio => !string.IsNullOrEmpty(AudioPath) && File.Exists(AudioPath);

        /// <summary>是否有可预览媒体（图片/音频）</summary>
        public bool HasMedia => HasImage || HasAudio;

        /// <summary>是否音频资产（BGM/音乐/音效/声线）</summary>
        public bool IsAudio => Asset.AssetType is AssetTypeEnum.Bgm or AssetTypeEnum.Music or AssetTypeEnum.SoundEffect or AssetTypeEnum.Voice;

        /// <summary>是否图片资产（角色/场景/道具）</summary>
        public bool IsImage => !IsAudio;

        /// <summary>全部描述（描述 + 单个正向/反向提示词）</summary>
        public string FullDescription { get; }

        /// <summary>子资产行（角色声线/变装、场景子面）</summary>
        public List<AssetCardItem> Children { get; } = new();

        public bool HasChildren => Children.Count > 0;

        /// <summary>解析资源绝对路径：遍历所有项目资源目录查找文件</summary>
        private string? ResolveResource(Resource? resource)
        {
            if (resource is null || string.IsNullOrEmpty(resource.RelativePath)) return null;
            foreach (var name in _projectNames)
            {
                try
                {
                    var abs = _resources.GetAbsolutePath(name, resource.RelativePath);
                    if (File.Exists(abs)) return abs;
                }
                catch { /* 忽略单个项目解析失败 */ }
            }
            return null;
        }

        private static string BuildRefProjects(Asset a)
        {
            if (a.ProjectRefs is { Count: > 0 })
            {
                // 已完结项目不显示（详情/表格中不展示已完结项目）
                var names = a.ProjectRefs
                    .Where(r => r.Project is not null && r.Project!.Status != ProjectStatusEnum.Completed)
                    .Select(r => r.Project!.Name)
                    .OrderBy(n => n)
                    .ToList();
                return names.Count == 0 ? "未绑定项目" : string.Join("、", names);
            }
            return "未绑定项目";
        }

        /// <summary>组合全部描述文本（资产的全部描述 = 描述 + 单个正向/反向提示词）</summary>
        private static string BuildFullDescription(Asset a)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(a.Description)) parts.Add(a.Description);

            // 提示词（单个正向 + 单个反向）
            if (!string.IsNullOrWhiteSpace(a.Prompt)) parts.Add($"提示词：{a.Prompt}");
            if (!string.IsNullOrWhiteSpace(a.NegativePrompt)) parts.Add($"反向：{a.NegativePrompt}");

            return parts.Count == 0 ? "暂无描述" : string.Join("\n", parts);
        }

        private static string GetDisplayName(Enum e)
        {
            var field = e.GetType().GetField(e.ToString());
            var attr = field?.GetCustomAttribute<DisplayAttribute>();
            return attr?.Name ?? e.ToString();
        }
    }
}
