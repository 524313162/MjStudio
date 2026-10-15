using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using MjStudio.Infrastructure;
using MjStudio.WebApi.Dtos;
using MjStudio.WebApi.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MjStudio.Host.ViewModels
{
    /// <summary>
    /// 分镜页 ViewModel：集管理 + 镜头 CRUD
    /// </summary>
    public class ShotsViewModel : ViewModelBase
    {
        private readonly IServiceProvider _services;
        private readonly IShotService _shots;
        private readonly IResourceService _resources;
        private readonly IAssetService _assets;
        private readonly IProjectService _projects;
        private readonly ResourceStorageService _resourceStorage;
        private readonly CurrentProject _current;
        private readonly GenerationEngine _engine;

        public ObservableCollection<Episode> Episodes { get; } = new();
        public ObservableCollection<Shot> Shots { get; } = new();

        // ===== 下拉选项 =====
        public List<string> CameraOptions { get; } = new()
        {
            "固定机位", "缓慢推近", "缓慢拉远", "跟随摇移", "环绕运镜", "航拍俯视", "手持晃动"
        };
        public List<string> ShotSizeOptions { get; } = new()
        {
            "全景", "中景", "近景", "特写", "大远景"
        };
        public List<string> TransitionOptions { get; } = new()
        {
            "硬切", "叠化", "淡入", "淡出", "闪白", "摇移承接"
        };

        /// <summary>本镜已绑定资产</summary>
        public ObservableCollection<ShotAssetRefItem> ShotAssets { get; } = new();

        private Episode? _selectedEpisode;
        public Episode? SelectedEpisode
        {
            get => _selectedEpisode;
            set { SetProperty(ref _selectedEpisode, value); LoadShots(); }
        }

        private Shot? _selectedShot;
        public Shot? SelectedShot
        {
            get => _selectedShot;
            set
            {
                if (SetProperty(ref _selectedShot, value))
                {
                    LoadFormFromSelected();
                    LoadShotAssets();
                }
            }
        }

        // ===== 镜头表单 =====
        private string _formTitle = "";
        public string FormTitle { get => _formTitle; set => SetProperty(ref _formTitle, value); }
        private string _formDescription = "";
        public string FormDescription { get => _formDescription; set => SetProperty(ref _formDescription, value); }
        private string _formCamera = "";
        public string FormCamera { get => _formCamera; set => SetProperty(ref _formCamera, value); }
        private string _formShotSize = "";
        public string FormShotSize { get => _formShotSize; set => SetProperty(ref _formShotSize, value); }
        private string _formTimeline = "";
        public string FormTimeline { get => _formTimeline; set => SetProperty(ref _formTimeline, value); }
        private string _formTransition = "";
        public string FormTransition { get => _formTransition; set => SetProperty(ref _formTransition, value); }
        private string _formPrompt = "";
        public string FormPrompt { get => _formPrompt; set => SetProperty(ref _formPrompt, value); }
        private string _formNegativePrompt = "";
        public string FormNegativePrompt { get => _formNegativePrompt; set => SetProperty(ref _formNegativePrompt, value); }

        // ===== 状态 =====
        private bool _isEditing;
        public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }
        private bool _isFormVisible;
        public bool IsFormVisible { get => _isFormVisible; set => SetProperty(ref _isFormVisible, value); }

        public RelayCommand RefreshCommand { get; }
        public RelayCommand CreateShotCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand CancelEditCommand { get; }
        public RelayCommand BindAssetCommand { get; }
        public RelayCommand UnbindAssetCommand { get; }
        public RelayCommand ExportOriginalCommand { get; }
        public RelayCommand ExportVoiceSwapCommand { get; }
        public RelayCommand GenerateVideoCommand { get; }
        public RelayCommand OpenBatchGenCommand { get; }

        // ===== 导出状态 =====
        private bool _isExporting;
        public bool IsExporting { get => _isExporting; set => SetProperty(ref _isExporting, value); }
        private string _exportStatusText = "";
        public string ExportStatusText { get => _exportStatusText; set => SetProperty(ref _exportStatusText, value); }

        // ===== 视频预览 =====
        private string? _videoPath;
        public string? VideoPath
        {
            get => _videoPath;
            set
            {
                if (SetProperty(ref _videoPath, value))
                {
                    OnPropertyChanged(nameof(VideoUri));
                    OnPropertyChanged(nameof(HasVideo));
                }
            }
        }
        public Uri? VideoUri => VideoPath is not null && File.Exists(VideoPath) ? new Uri(VideoPath) : null;
        public bool HasVideo => VideoPath is not null && File.Exists(VideoPath);

        private bool _isGeneratingVideo;
        public bool IsGeneratingVideo { get => _isGeneratingVideo; set => SetProperty(ref _isGeneratingVideo, value); }

        public ShotsViewModel(IServiceProvider services)
        {
            _services = services;
            _shots = services.GetRequiredService<IShotService>();
            _resources = services.GetRequiredService<IResourceService>();
            _assets = services.GetRequiredService<IAssetService>();
            _projects = services.GetRequiredService<IProjectService>();
            _resourceStorage = services.GetRequiredService<ResourceStorageService>();
            _current = services.GetRequiredService<CurrentProject>();
            _engine = services.GetRequiredService<GenerationEngine>();

            RefreshCommand = new RelayCommand(Refresh);
            CreateShotCommand = new RelayCommand(StartCreateShot);
            SaveCommand = new RelayCommand(Save);
            DeleteCommand = new RelayCommand(Delete);
            CancelEditCommand = new RelayCommand(CancelEdit);
            BindAssetCommand = new RelayCommand(BindAsset);
            UnbindAssetCommand = new RelayCommand(UnbindAsset);
            ExportOriginalCommand = new RelayCommand(ExportOriginal, () => CanExport);
            ExportVoiceSwapCommand = new RelayCommand(ExportVoiceSwap, () => CanExport);
            GenerateVideoCommand = new RelayCommand(GenerateVideo, () => CanGenerateVideo);
            OpenBatchGenCommand = new RelayCommand(OpenBatchGen, () => _current.IsLoaded);

            Refresh();
        }

        public void Refresh()
        {
            Episodes.Clear();
            Shots.Clear();
            IsFormVisible = false;
            SelectedShot = null;
            ClearForm();
            if (!_current.IsLoaded) return;
            try
            {
                var pid = _resources.GetProjectIdAsync().GetAwaiter().GetResult();
                var list = _shots.GetEpisodesAsync(pid).GetAwaiter().GetResult();
                foreach (var e in list.OrderBy(x => x.EpisodeNo)) Episodes.Add(e);
                // 进入分镜页默认选中第一集（触发 LoadShots），再选中第一个镜头
                if (Episodes.Count > 0)
                {
                    SelectedEpisode = Episodes[0];
                    if (Shots.Count > 0)
                        SelectedShot = Shots[0];
                }
            }
            catch { /* 忽略 */ }
        }

        private void LoadShots()
        {
            Shots.Clear();
            IsFormVisible = false;
            SelectedShot = null;
            ClearForm();
            if (SelectedEpisode is null) return;
            try
            {
                var list = _shots.GetShotsAsync(SelectedEpisode.Id).GetAwaiter().GetResult();
                foreach (var s in list.OrderBy(x => x.ShotNo)) Shots.Add(s);
            }
            catch { /* 忽略 */ }
        }

        private void StartCreateShot()
        {
            if (SelectedEpisode is null)
            {
                MessageBox.Show("请先选择一集", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // 先清空选中（触发 LoadFormFromSelected → ClearForm），再设置默认值，避免被清空
            SelectedShot = null;
            ClearForm();
            FormCamera = "固定机位"; // 新增镜头时运镜默认固定机位
            IsEditing = false;
            IsFormVisible = true;
        }

        private void LoadFormFromSelected()
        {
            if (SelectedShot is null) { ClearForm(); return; }
            IsEditing = true;
            IsFormVisible = true;
            FormTitle = SelectedShot.Title ?? "";
            FormDescription = SelectedShot.VideoContent ?? "";
            FormCamera = SelectedShot.Camera ?? "";
            FormShotSize = SelectedShot.ShotSize ?? "";
            FormTimeline = SelectedShot.Timeline ?? "";
            FormTransition = SelectedShot.Transition ?? "";
            FormPrompt = SelectedShot.VideoPromptCn ?? "";
            FormNegativePrompt = SelectedShot.VideoNegativePromptEn ?? "";
            LoadVideoPath();
        }

        /// <summary>加载当前镜头的视频路径</summary>
        private void LoadVideoPath()
        {
            if (SelectedShot?.VideoResource is { RelativePath: not null } res)
            {
                try
                {
                    VideoPath = _resourceStorage.GetAbsolutePath(_current.Require(), res.RelativePath);
                    return;
                }
                catch { /* 忽略 */ }
            }
            VideoPath = null;
        }

        /// <summary>加载本镜已绑定资产</summary>
        private void LoadShotAssets()
        {
            ShotAssets.Clear();
            if (SelectedShot is null) return;
            try
            {
                var shot = _shots.GetShotAsync(SelectedShot.Id).GetAwaiter().GetResult();
                if (shot?.AssetRefs is null) return;
                foreach (var r in shot.AssetRefs.OrderBy(x => x.MediaIndex))
                {
                    var item = ShotAssetRefItem.From(r);
                    item.PropertyChanged += OnAudioRangeChanged;
                    ShotAssets.Add(item);
                }
            }
            catch { /* 忽略 */ }
        }

        /// <summary>音频（BGM/音效）播放时间段编辑后持久化</summary>
        private void OnAudioRangeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ShotAssetRefItem.StartText) or nameof(ShotAssetRefItem.EndText))
                SaveAudioRanges();
        }

        /// <summary>把当前本镜资产的播放时间段写回（全量覆盖）</summary>
        private void SaveAudioRanges()
        {
            if (SelectedShot is null) return;
            try
            {
                var refs = ShotAssets
                    .Select(i => (i.AssetId, i.MediaIndex, i.RefType, ParseRange(i.StartText), ParseRange(i.EndText)))
                    .ToList();
                _shots.SetAssetRefsAsync(SelectedShot.Id, refs).GetAwaiter().GetResult();
            }
            catch { /* 忽略 */ }
        }

        private static float? ParseRange(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return float.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        /// <summary>绑定资产：打开对话框选择要绑定的资产</summary>
        private void BindAsset()
        {
            if (SelectedShot is null) return;
            if (!_current.IsLoaded)
            {
                MessageBox.Show("请先加载项目", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                var projectNames = _projects.ListProjectNamesAsync().GetAwaiter().GetResult();
                var allAssets = _assets.GetAllWithRefsAsync().GetAwaiter().GetResult();
                var boundIds = SelectedShot.AssetRefs?.Select(r => r.AssetId).ToHashSet() ?? new HashSet<long>();

                // 只取角色/场景/BGM/音效，排除子资产，排除已绑定的
                var available = allAssets
                    .Where(x => x.AssetType is AssetTypeEnum.Character or AssetTypeEnum.Scene or AssetTypeEnum.Bgm or AssetTypeEnum.SoundEffect
                        && x.ParentAssetId is null && !boundIds.Contains(x.Id))
                    .ToList();

                if (available.Count == 0)
                {
                    MessageBox.Show("没有可绑定的资产，请先在「资产」页创建", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new Views.BindAssetDialog(available, _resourceStorage, projectNames);
                dialog.Owner = System.Windows.Application.Current.MainWindow;
                if (dialog.ShowDialog() == true && dialog.SelectedAssets is { Count: > 0 } selected)
                {
                    // 合并已绑定 + 新选，重新排定 MediaIndex（保留已有 BGM/音效的播放时间段）
                    var existingRefs = SelectedShot.AssetRefs?.ToList() ?? new List<ShotAssetRef>();
                    var newRefs = existingRefs
                        .Select(r => (r.AssetId, r.MediaIndex, r.RefType ?? "", r.StartRange, r.EndRange))
                        .ToList();
                    int nextIndex = newRefs.Count > 0 ? newRefs.Max(r => r.MediaIndex) + 1 : 1;
                    foreach (var a in selected)
                    {
                        var refType = a.AssetType switch
                        {
                            AssetTypeEnum.Character => "character",
                            AssetTypeEnum.Scene => "scene",
                            AssetTypeEnum.Bgm => "bgm",
                            AssetTypeEnum.SoundEffect => "sfx",
                            _ => "other"
                        };
                        newRefs.Add((a.Id, nextIndex++, refType, (float?)null, (float?)null));
                    }
                    _shots.SetAssetRefsAsync(SelectedShot.Id, newRefs).GetAwaiter().GetResult();
                    LoadShotAssets();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("绑定资产失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>解绑资产：从镜头移除该资产引用</summary>
        private void UnbindAsset(object? param)
        {
            if (param is not ShotAssetRefItem item || SelectedShot is null) return;
            try
            {
                var shot = _shots.GetShotAsync(SelectedShot.Id).GetAwaiter().GetResult();
                if (shot?.AssetRefs is null) return;
                var remaining = shot.AssetRefs.Where(r => r.AssetId != item.AssetId)
                    .Select(r => (r.AssetId, r.MediaIndex, r.RefType ?? "", r.StartRange, r.EndRange))
                    .ToList();
                _shots.SetAssetRefsAsync(SelectedShot.Id, remaining).GetAwaiter().GetResult();
                LoadShotAssets();
            }
            catch (Exception ex)
            {
                MessageBox.Show("解绑失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearForm()
        {
            IsEditing = false;
            FormTitle = ""; FormDescription = "";
            FormCamera = ""; FormShotSize = "";
            FormTimeline = ""; FormTransition = "";
            FormPrompt = ""; FormNegativePrompt = "";
            VideoPath = null;
        }

        private void CancelEdit()
        {
            ClearForm();
            IsFormVisible = false;
            SelectedShot = null;
        }

        private Shot BuildShotFromForm()
        {
            var shot = new Shot
            {
                Title = FormTitle.Trim(),
                VideoContent = FormDescription,
                Camera = FormCamera,
                ShotSize = FormShotSize,
                Timeline = FormTimeline,
                Transition = FormTransition,
                VideoPromptCn = FormPrompt,
                VideoNegativePromptEn = FormNegativePrompt
            };
            return shot;
        }

        private void Save()
        {
            if (SelectedEpisode is null)
            {
                MessageBox.Show("请先选择一集", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(FormTitle))
            {
                MessageBox.Show("请输入镜头标题", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                if (IsEditing && SelectedShot is not null)
                {
                    var shot = BuildShotFromForm();
                    shot.Id = SelectedShot.Id;
                    shot.EpisodeId = SelectedShot.EpisodeId;
                    shot.ShotNo = SelectedShot.ShotNo;
                    shot.CreatedTime = SelectedShot.CreatedTime;
                    _shots.UpdateShotAsync(shot).GetAwaiter().GetResult();
                    MessageBox.Show("镜头已保存", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var shot = BuildShotFromForm();
                    shot.EpisodeId = SelectedEpisode.Id;
                    shot.ShotNo = Shots.Count + 1;
                    _shots.CreateShotAsync(shot).GetAwaiter().GetResult();
                    MessageBox.Show("镜头已创建", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                LoadShots();
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Delete()
        {
            if (SelectedShot is null) return;
            if (MessageBox.Show($"确定删除镜头「{SelectedShot.Title}」？", "MjStudio", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try
            {
                _shots.DeleteShotAsync(SelectedShot.Id).GetAwaiter().GetResult();
                LoadShots();
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== 导出（复用 GenerationEngine 的 videoCompose / videoExport 接口，与 API 同一套代码） ====================

        private bool CanExport => _current.IsLoaded && SelectedEpisode is not null && Shots.Count > 0 && !IsExporting;

        /// <summary>
        /// 导出前校验：当前集所有镜头必须都有视频，缺一个都不能导出。
        /// 有缺失时弹窗列出具体缺哪个镜头，并自动选中第一个缺失的镜头，返回 false。
        /// </summary>
        private bool ValidateAllShotsHaveVideo()
        {
            if (SelectedEpisode is null) return false;
            // 直接用内存中的 Shots 集合（与列表框同一批实例，选中才能生效）
            var shots = Shots.OrderBy(s => s.ShotNo).ToList();
            var missing = shots.Where(s => s.VideoResourceId is null).ToList();
            if (missing.Count == 0) return true;

            var lines = string.Join("\n", missing.Select(s => $"  • 镜头#{s.ShotNo}「{s.Title}」"));
            MessageBox.Show(
                $"无法导出：当前集共 {shots.Count} 个镜头，有 {missing.Count} 个镜头还没有视频，缺一个都不能导出：\n\n{lines}\n\n已为你选中第一个缺失的镜头，请先生成视频后再导出。",
                "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);

            // 自动选中第一个缺失的镜头（Shots 集合与 GetShotsAsync 返回同一批实例，可直接赋值）
            SelectedShot = missing[0];
            return false;
        }

        /// <summary>原声导出：将当前集所有镜头视频按顺序拼接成一个视频，保存到用户选择的位置</summary>
        private async void ExportOriginal()
        {
            if (!CanExport) return;
            try
            {
                if (!ValidateAllShotsHaveVideo()) return;
                var videos = CollectShotVideoPaths();
                var savePath = AskSavePath("原声导出");
                if (savePath is null) return;

                IsExporting = true;
                ExportStatusText = "正在合并视频…";
                var task = _engine.Submit("videoCompose", new VideoComposeRequest { Videos = videos });
                var result = await WaitForTaskAsync(task, "合并视频");
                if (result.Status != "completed" || string.IsNullOrEmpty(result.RelativePath))
                    throw new InvalidOperationException(result.Message ?? "合并失败");

                CopyToSavePath(result.RelativePath, savePath);
                ExportStatusText = "原声导出完成";
                MessageBox.Show($"原声导出完成：\n{savePath}", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ExportStatusText = "导出失败";
                MessageBox.Show("原声导出失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsExporting = false;
            }
        }

        /// <summary>换声导出：每个镜头视频做声音转换（用所选角色声线克隆），再拼接成一个视频，保存到用户选择的位置</summary>
        private async void ExportVoiceSwap()
        {
            if (!CanExport) return;
            try
            {
                if (!ValidateAllShotsHaveVideo()) return;
                var videos = CollectShotVideoPaths();

                var voiceRel = PickVoice();
                if (voiceRel is null) return;

                var savePath = AskSavePath("换声导出");
                if (savePath is null) return;

                IsExporting = true;
                var swapped = new List<string>();
                for (int i = 0; i < videos.Count; i++)
                {
                    ExportStatusText = $"正在换声 镜头 {i + 1}/{videos.Count}…";
                    var task = _engine.Submit("videoExport", new VideoExportRequest
                    {
                        SourceVideo = videos[i],
                        TargetVoice = voiceRel
                    });
                    var result = await WaitForTaskAsync(task, "换声");
                    if (result.Status != "completed" || string.IsNullOrEmpty(result.RelativePath))
                        throw new InvalidOperationException(result.Message ?? "换声失败");
                    swapped.Add(result.RelativePath);
                }

                ExportStatusText = "正在合并换声后的视频…";
                var composeTask = _engine.Submit("videoCompose", new VideoComposeRequest { Videos = swapped });
                var composeResult = await WaitForTaskAsync(composeTask, "合并视频");
                if (composeResult.Status != "completed" || string.IsNullOrEmpty(composeResult.RelativePath))
                    throw new InvalidOperationException(composeResult.Message ?? "合并失败");

                CopyToSavePath(composeResult.RelativePath, savePath);
                ExportStatusText = "换声导出完成";
                MessageBox.Show($"换声导出完成：\n{savePath}", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ExportStatusText = "导出失败";
                MessageBox.Show("换声导出失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsExporting = false;
            }
        }

        /// <summary>收集当前集所有镜头的视频相对路径（按镜头号排序）</summary>
        private List<string> CollectShotVideoPaths()
        {
            if (SelectedEpisode is null) return new List<string>();
            var shots = _shots.GetShotsAsync(SelectedEpisode.Id).GetAwaiter().GetResult()
                .OrderBy(s => s.ShotNo).ToList();
            var result = new List<string>();
            foreach (var s in shots)
            {
                var full = _shots.GetShotAsync(s.Id).GetAwaiter().GetResult();
                var rel = full?.VideoResource?.RelativePath;
                if (string.IsNullOrEmpty(rel))
                    throw new InvalidOperationException($"镜头#{s.ShotNo}「{s.Title}」还没有视频，请先生成视频");
                result.Add(rel);
            }
            return result;
        }

        /// <summary>弹出对话框选择角色声线，返回声线音频的相对路径（取消返回 null）</summary>
        private string? PickVoice()
        {
            var voices = _assets.GetAllWithRefsAsync(AssetTypeEnum.Voice).GetAwaiter().GetResult()
                .Where(v => v.Resource is not null && !string.IsNullOrEmpty(v.Resource.RelativePath))
                .ToList();
            if (voices.Count == 0)
            {
                MessageBox.Show("没有可用的角色声线，请先在「资产」页创建声线资产并挂载音频", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            var dialog = new Views.VoiceSelectDialog(voices);
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            if (dialog.ShowDialog() == true && dialog.SelectedVoice is not null)
                return dialog.SelectedVoice.Resource!.RelativePath;
            return null;
        }

        /// <summary>弹出保存位置对话框</summary>
        private string? AskSavePath(string title)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = title,
                Filter = "MP4 视频 (*.mp4)|*.mp4",
                FileName = $"{_current.Require()}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4",
                DefaultExt = ".mp4"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        /// <summary>轮询任务直到完成/失败</summary>
        private async Task<GenerationTask> WaitForTaskAsync(GenerationTask task, string label)
        {
            while (true)
            {
                var t = await _engine.PollAsync(task.TaskId, timeoutSec: 60);
                if (t.Status is "completed" or "failed") return t;
                ExportStatusText = $"{label}中…";
            }
        }

        /// <summary>把资源目录中的产物复制到用户选择的保存位置</summary>
        private void CopyToSavePath(string relativePath, string savePath)
        {
            var projectName = _current.Require();
            var abs = _resourceStorage.GetAbsolutePath(projectName, relativePath);
            if (!File.Exists(abs))
                throw new FileNotFoundException($"产物不存在: {abs}");
            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.Copy(abs, savePath, overwrite: true);
        }

        // ==================== 批量生成 ====================

        /// <summary>打开批量生成弹窗（图片/声音/视频 3 个标签页）</summary>
        private void OpenBatchGen()
        {
            if (!_current.IsLoaded)
            {
                MessageBox.Show("请先加载项目", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var vm = new BatchGenViewModel(_services);
            var dialog = new Views.BatchGenDialog(vm)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }

        // ==================== 视频生成 ====================

        private bool CanGenerateVideo => _current.IsLoaded && SelectedShot is not null && !IsGeneratingVideo;

        /// <summary>生成/重新生成当前镜头的视频</summary>
        private async void GenerateVideo()
        {
            if (!CanGenerateVideo || SelectedShot is null) return;
            try
            {
                // 1. 收集参考图（角色/场景/道具，按 MediaIndex 排序）
                var shot = _shots.GetShotAsync(SelectedShot.Id).GetAwaiter().GetResult();
                var imageRefs = shot?.AssetRefs
                    .Where(r => r.RefType is not "bgm" and not "sfx")
                    .OrderBy(r => r.MediaIndex)
                    .ToList() ?? new List<ShotAssetRef>();

                if (imageRefs.Count == 0)
                {
                    MessageBox.Show("请先绑定参考图资产（角色/场景/道具）", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var imagePaths = new List<string>();
                foreach (var refItem in imageRefs)
                {
                    var rel = refItem.Asset?.Resource?.RelativePath;
                    if (!string.IsNullOrEmpty(rel)) imagePaths.Add(rel);
                }

                if (imagePaths.Count == 0)
                {
                    MessageBox.Show("绑定的资产没有可用的参考图，请检查资产是否已挂载图片", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 2. 构建视频生成请求
                var prompt = SelectedShot.VideoPromptCn ?? SelectedShot.VideoContent ?? "";
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    MessageBox.Show("请填写视频提示词", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var seconds = (int)(SelectedShot.Duration ?? 10);
                var request = new VideoGenRequest
                {
                    Prompt = prompt,
                    Images = imagePaths,
                    Seconds = seconds
                };

                // 3. 提交并轮询
                IsGeneratingVideo = true;
                ExportStatusText = "正在生成视频…";
                var task = _engine.Submit("video", request);
                var result = await WaitForTaskAsync(task, "生成视频");

                if (result.Status != "completed" || string.IsNullOrEmpty(result.RelativePath))
                    throw new InvalidOperationException(result.Message ?? "视频生成失败");

                // 4. 挂载视频到镜头
                await _shots.AttachVideoAsync(SelectedShot.Id, result.RelativePath, ResourcePurposeEnum.ShotVideo);

                // 5. 刷新 UI
                var shotNo = SelectedShot.ShotNo;
                LoadShots();
                var reselected = Shots.FirstOrDefault(s => s.ShotNo == shotNo);
                if (reselected is not null) SelectedShot = reselected;

                ExportStatusText = "视频生成完成";
                MessageBox.Show("视频生成完成", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ExportStatusText = "视频生成失败";
                MessageBox.Show("视频生成失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsGeneratingVideo = false;
            }
        }
    }
}
