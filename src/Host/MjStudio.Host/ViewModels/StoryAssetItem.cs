using System.IO;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using MjStudio.Infrastructure;

namespace MjStudio.Host.ViewModels
{
    /// <summary>
    /// 剧本页角色资产项：包装 Asset 供剧本页角色/配角资产区展示。
    /// 负责解析资产图片路径、类型显示名。
    /// </summary>
    public class StoryAssetItem : ViewModelBase
    {
        private readonly ResourceStorageService _resources;
        private readonly IReadOnlyList<string> _projectNames;

        public Asset Asset { get; }

        /// <summary>资产 ID（用于 CharacterList 关联）</summary>
        public long AssetId => Asset.Id;

        public StoryAssetItem(Asset asset, ResourceStorageService resources, IReadOnlyList<string> projectNames)
        {
            Asset = asset;
            _resources = resources;
            _projectNames = projectNames;

            TypeDisplay = GetDisplayName(asset.AssetType);
            ImagePath = ResolveImage(asset.Resource);
        }

        public string Name => Asset.Name ?? "";
        public string TypeDisplay { get; }
        public string Description => Asset.Description ?? "";

        /// <summary>图片绝对路径（无则 null）</summary>
        public string? ImagePath { get; }

        public bool HasImage => !string.IsNullOrEmpty(ImagePath) && File.Exists(ImagePath);

        /// <summary>解析资源绝对路径：遍历所有项目资源目录查找文件</summary>
        private string? ResolveImage(Resource? resource)
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

        private static string GetDisplayName(Enum e)
        {
            var field = e.GetType().GetField(e.ToString());
            var attr = field?.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false)
                .FirstOrDefault() as System.ComponentModel.DataAnnotations.DisplayAttribute;
            return attr?.Name ?? e.ToString();
        }
    }
}
