using System.Collections.ObjectModel;
using System.Windows;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Shared;
using MjStudio.Infrastructure;
using MjStudio.WebApi.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MjStudio.Host.ViewModels
{
    /// <summary>
    /// 批量生成弹窗 ViewModel：3 个标签页（图片/声音/视频），
    /// 每页展示项目内全部资产/分镜卡片，支持「全部生成（跳过已存在）」「单项生成」「单项清理」。
    /// 直接调用共享 DI 中的 <see cref="BatchGenService"/> 与 <see cref="GenerationEngine"/>（与 API 同一套代码）。
    /// </summary>
    public class BatchGenViewModel : ViewModelBase
    {
        private readonly IServiceProvider _services;
        private readonly BatchGenService _svc;
        private readonly GenerationEngine _engine;
        private readonly ResourceStorageService _storage;
        private readonly CurrentProject _current;

        // ===== 三个标签页的卡片集合 =====
        public ObservableCollection<BatchGenCardItem> ImageCards { get; } = new();
        public ObservableCollection<BatchGenCardItem> AudioCards { get; } = new();
        public ObservableCollection<BatchGenCardItem> VideoCards { get; } = new();

        // ===== 当前标签页 =====
        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        // ===== 状态 =====
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                    OnPropertyChanged(nameof(StatusText));
            }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _isBusy ? _statusText : "";
            set => SetProperty(ref _statusText, value);
        }

        public RelayCommand LoadCommand { get; }
        public RelayCommand GenerateAllCommand { get; }
        public RelayCommand CloseCommand { get; }

        /// <summary>弹窗是否确认关闭（由 code-behind 读取）</summary>
        public bool IsConfirmed { get; private set; }

        public BatchGenViewModel(IServiceProvider services)
        {
            _services = services;
            _svc = services.GetRequiredService<BatchGenService>();
            _engine = services.GetRequiredService<GenerationEngine>();
            _storage = services.GetRequiredService<ResourceStorageService>();
            _current = services.GetRequiredService<CurrentProject>();

            LoadCommand = new RelayCommand(LoadAll);
            GenerateAllCommand = new RelayCommand(GenerateAll, () => !IsBusy);
            CloseCommand = new RelayCommand(Close);

            LoadAll();
        }

        // ==================== 加载 ====================

        /// <summary>加载三个模块的全部卡片</summary>
        private void LoadAll()
        {
            if (!_current.IsLoaded)
            {
                MessageBox.Show("未加载项目，无法批量生成", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                LoadModule("image", ImageCards);
                LoadModule("audio", AudioCards);
                LoadModule("video", VideoCards);
            }
            catch (Exception ex)
            {
                MessageBox.Show("加载失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadModule(string module, ObservableCollection<BatchGenCardItem> target)
        {
            target.Clear();
            var items = _svc.GetItemsAsync(module).GetAwaiter().GetResult();
            var projectName = _current.Require();
            foreach (var it in items)
            {
                var card = BatchGenCardItem.From(it);
                // 图片模块：把相对路径解析为绝对路径用于缩略图
                if (module == "image" && !string.IsNullOrEmpty(it.RelativePath))
                    card.ImagePath = _storage.GetAbsolutePath(projectName, it.RelativePath);
                // 视频模块：把参考图相对路径解析为绝对路径，展示分镜绑定的资产缩略图
                if (module == "video")
                    card.ReferenceImages = it.ReferenceImagePaths
                        .Where(p => !string.IsNullOrEmpty(p))
                        .Select(p => _storage.GetAbsolutePath(projectName, p))
                        .Where(p => System.IO.File.Exists(p))
                        .ToList();
                target.Add(card);
            }
        }

        // ==================== 全部生成（跳过已存在） ====================

        private async void GenerateAll()
        {
            var (module, cards) = SelectedTabIndex switch
            {
                0 => ("image", ImageCards),
                1 => ("audio", AudioCards),
                _ => ("video", VideoCards)
            };

            var pending = cards.Where(c => !c.HasContent && !c.IsGenerating).ToList();
            if (pending.Count == 0)
            {
                MessageBox.Show("该模块所有内容均已生成，无需重复生成", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsBusy = true;
            try
            {
                // 串行逐个生成：当前项 loading，完成后变绿，再处理下一个
                int done = 0;
                foreach (var card in pending)
                {
                    card.IsGenerating = true;
                    card.StatusText = "生成中…";
                    StatusText = $"生成中 {done + 1}/{pending.Count}：{card.Name}…";
                    try
                    {
                        var task = _svc.SubmitForItem(ToItem(card));
                        var result = await WaitForTaskAsync(task);
                        if (result.Status == "completed" && !string.IsNullOrEmpty(result.RelativePath))
                        {
                            await _svc.AttachResultAsync(ToItem(card), result);
                            card.HasContent = true;
                            card.StatusText = "已生成";
                            if (module == "image" && !string.IsNullOrEmpty(result.RelativePath))
                                card.ImagePath = _storage.GetAbsolutePath(_current.Require(), result.RelativePath);
                        }
                        else
                        {
                            card.StatusText = "失败：" + (result.Message ?? "未知错误");
                        }
                    }
                    catch (Exception ex)
                    {
                        card.StatusText = "失败：" + ex.Message;
                    }
                    finally
                    {
                        card.IsGenerating = false;
                    }
                    done++;
                }

                StatusText = $"完成：{done} 个任务已处理";
                MessageBox.Show($"批量生成完成，共处理 {done} 个任务", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("批量生成失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ==================== 单项生成 ====================

        /// <summary>由 code-behind 调用：生成单个卡片</summary>
        public async void GenerateSingle(BatchGenCardItem card)
        {
            if (card.IsGenerating) return;
            if (string.IsNullOrWhiteSpace(card.Prompt))
            {
                MessageBox.Show($"「{card.Name}」缺少提示词，无法生成，请先在资产/分镜页填写", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            card.IsGenerating = true;
            card.StatusText = "生成中…";
            try
            {
                var task = _svc.SubmitForItem(ToItem(card));
                var result = await WaitForTaskAsync(task);
                if (result.Status == "completed" && !string.IsNullOrEmpty(result.RelativePath))
                {
                    await _svc.AttachResultAsync(ToItem(card), result);
                    card.HasContent = true;
                    card.StatusText = "已生成";
                    if (card.ModuleType == "image")
                        card.ImagePath = _storage.GetAbsolutePath(_current.Require(), result.RelativePath);
                }
                else
                {
                    card.StatusText = "失败：" + (result.Message ?? "未知错误");
                }
            }
            catch (Exception ex)
            {
                card.StatusText = "失败：" + ex.Message;
            }
            finally
            {
                card.IsGenerating = false;
            }
        }

        // ==================== 单项清理 ====================

        /// <summary>由 code-behind 调用：清理单个卡片内容</summary>
        public async void ClearSingle(BatchGenCardItem card)
        {
            if (card.IsGenerating) return;
            if (MessageBox.Show($"确定清理「{card.Name}」的已生成内容？\n清理后可重新生成。", "MjStudio",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try
            {
                await _svc.ClearAsync(ToItem(card));
                card.HasContent = false;
                card.ImagePath = null;
                card.StatusText = "已清理";
            }
            catch (Exception ex)
            {
                MessageBox.Show("清理失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== 辅助 ====================

        /// <summary>把卡片转回服务层 BatchGenItem（携带生成参数）</summary>
        private WebApi.Dtos.BatchGenItem ToItem(BatchGenCardItem card) => new()
        {
            Id = card.Id,
            Name = card.Name,
            TypeDisplay = card.TypeDisplay,
            Description = card.Description,
            ModuleType = card.ModuleType,
            AssetType = card.AssetType,
            ShotNo = card.ShotNo,
            EpisodeNo = card.EpisodeNo,
            Prompt = card.Prompt,
            NegativePrompt = card.NegativePrompt,
            Duration = card.Duration,
            ReferenceImagePaths = card.ReferenceImagePaths
        };

        private async Task<GenerationTask> WaitForTaskAsync(GenerationTask task)
        {
            while (true)
            {
                var t = await _engine.PollAsync(task.TaskId, timeoutSec: 60);
                if (t.Status is "completed" or "failed") return t;
            }
        }

        private void Close()
        {
            IsConfirmed = true;
        }
    }
}
