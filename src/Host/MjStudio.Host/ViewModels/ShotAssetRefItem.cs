using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;

namespace MjStudio.Host.ViewModels
{
    /// <summary>
    /// 分镜页「本镜资产」展示项：包装 ShotAssetRef，显示资产名/类型/参考图序号。
    /// </summary>
    public class ShotAssetRefItem
    {
        public long AssetId { get; init; }
        public string Name { get; init; } = "";
        public string TypeDisplay { get; init; } = "";
        public int MediaIndex { get; init; }
        public string RefType { get; init; } = "";

        /// <summary>参考图序号显示文本</summary>
        public string MediaIndexText => $"参考图 #{MediaIndex}";

        public static ShotAssetRefItem From(ShotAssetRef refItem)
        {
            var asset = refItem.Asset;
            return new ShotAssetRefItem
            {
                AssetId = refItem.AssetId,
                Name = asset?.Name ?? $"资产#{refItem.AssetId}",
                TypeDisplay = asset is null ? "" : GetDisplayName(asset.AssetType),
                MediaIndex = refItem.MediaIndex,
                RefType = refItem.RefType ?? ""
            };
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
