using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;

namespace MjStudio.Host.ViewModels
{
    /// <summary>
    /// 分镜页「本镜资产」展示项：包装 ShotAssetRef，显示资产名/类型/参考图序号。
    /// </summary>
    public class ShotAssetRefItem : ViewModelBase
    {
        public long RefId { get; init; }
        public long AssetId { get; init; }
        public string Name { get; init; } = "";
        public string TypeDisplay { get; init; } = "";
        public int MediaIndex { get; init; }
        public string RefType { get; init; } = "";

        /// <summary>是否音频类引用（BGM/音效）</summary>
        public bool IsAudio => RefType is "bgm" or "sfx";

        /// <summary>参考图序号显示文本（音频类不显示）</summary>
        public string MediaIndexText => IsAudio ? "" : $"参考图 #{MediaIndex}";

        /// <summary>开始播放时间（秒，文本，供 TextBox 编辑）</summary>
        private string _startText = "";
        public string StartText { get => _startText; set => SetProperty(ref _startText, value); }

        /// <summary>结束播放时间（秒，文本，供 TextBox 编辑）</summary>
        private string _endText = "";
        public string EndText { get => _endText; set => SetProperty(ref _endText, value); }

        public static ShotAssetRefItem From(ShotAssetRef refItem)
        {
            var asset = refItem.Asset;
            return new ShotAssetRefItem
            {
                RefId = refItem.Id,
                AssetId = refItem.AssetId,
                Name = asset?.Name ?? $"资产#{refItem.AssetId}",
                TypeDisplay = asset is null ? "" : GetDisplayName(asset.AssetType),
                MediaIndex = refItem.MediaIndex,
                RefType = refItem.RefType ?? "",
                StartText = refItem.StartRange is float s ? s.ToString("0.#") : "",
                EndText = refItem.EndRange is float e ? e.ToString("0.#") : ""
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
