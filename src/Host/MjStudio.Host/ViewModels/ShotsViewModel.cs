using System.Collections.ObjectModel;
using System.Windows;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
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
        private readonly CurrentProject _current;

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
        public List<string> CameraFacingOptions { get; } = new()
        {
            "镜头朝东", "镜头朝南", "镜头朝西", "镜头朝北", "镜头朝上", "镜头朝下"
        };
        public List<string> TransitionOptions { get; } = new()
        {
            "硬切", "叠化", "淡入", "淡出", "闪白", "摇移承接"
        };

        /// <summary>BGM 资产（AssetType.Bgm）</summary>
        public ObservableCollection<Asset> BgmAssets { get; } = new();
        /// <summary>音效资产（AssetType.SoundEffect）</summary>
        public ObservableCollection<Asset> SfxAssets { get; } = new();

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
                    LoadFormFromSelected();
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
        private string _formCameraFacing = "";
        public string FormCameraFacing { get => _formCameraFacing; set => SetProperty(ref _formCameraFacing, value); }
        private string _formTimeline = "";
        public string FormTimeline { get => _formTimeline; set => SetProperty(ref _formTimeline, value); }
        private string _formBgm = "";
        public string FormBgm { get => _formBgm; set => SetProperty(ref _formBgm, value); }
        private string _formBgmRange = "";
        public string FormBgmRange { get => _formBgmRange; set => SetProperty(ref _formBgmRange, value); }
        private string _formAmbientSfx = "";
        public string FormAmbientSfx { get => _formAmbientSfx; set => SetProperty(ref _formAmbientSfx, value); }
        private string _formSfxRange = "";
        public string FormSfxRange { get => _formSfxRange; set => SetProperty(ref _formSfxRange, value); }
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

        public ShotsViewModel(IServiceProvider services)
        {
            _services = services;
            _shots = services.GetRequiredService<IShotService>();
            _resources = services.GetRequiredService<IResourceService>();
            _assets = services.GetRequiredService<IAssetService>();
            _current = services.GetRequiredService<CurrentProject>();

            RefreshCommand = new RelayCommand(Refresh);
            CreateShotCommand = new RelayCommand(StartCreateShot);
            SaveCommand = new RelayCommand(Save);
            DeleteCommand = new RelayCommand(Delete);
            CancelEditCommand = new RelayCommand(CancelEdit);

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
            }
            catch { /* 忽略 */ }

            LoadAudioAssets();
        }

        /// <summary>加载 BGM 与音效资产（仅当前项目绑定的资产，供下拉选择）</summary>
        private void LoadAudioAssets()
        {
            BgmAssets.Clear();
            SfxAssets.Clear();
            if (!_current.IsLoaded) return;
            try
            {
                var pid = _resources.GetProjectIdAsync().GetAwaiter().GetResult();
                var list = _assets.GetByProjectAsync(pid).GetAwaiter().GetResult();
                foreach (var a in list.Where(x => x.AssetType == AssetTypeEnum.Bgm && x.ParentAssetId is null))
                    BgmAssets.Add(a);
                foreach (var a in list.Where(x => x.AssetType == AssetTypeEnum.SoundEffect && x.ParentAssetId is null))
                    SfxAssets.Add(a);
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
            FormCameraFacing = SelectedShot.CameraFacing ?? "";
            FormTimeline = SelectedShot.Timeline ?? "";
            FormBgm = SelectedShot.Bgm ?? "";
            FormBgmRange = SelectedShot.BgmRange ?? "";
            FormAmbientSfx = SelectedShot.AmbientSfx ?? "";
            FormSfxRange = SelectedShot.SfxRange ?? "";
            FormTransition = SelectedShot.Transition ?? "";
            FormPrompt = SelectedShot.VideoPromptCn ?? "";
            FormNegativePrompt = SelectedShot.VideoNegativePromptEn ?? "";
        }

        private void ClearForm()
        {
            IsEditing = false;
            FormTitle = ""; FormDescription = "";
            FormCamera = ""; FormShotSize = ""; FormCameraFacing = "";
            FormTimeline = ""; FormBgm = ""; FormBgmRange = "";
            FormAmbientSfx = ""; FormSfxRange = ""; FormTransition = "";
            FormPrompt = ""; FormNegativePrompt = "";
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
                CameraFacing = FormCameraFacing,
                Timeline = FormTimeline,
                Bgm = FormBgm,
                BgmRange = FormBgmRange,
                AmbientSfx = FormAmbientSfx,
                SfxRange = FormSfxRange,
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
    }
}
