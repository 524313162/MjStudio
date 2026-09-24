using System.Collections.ObjectModel;
using System.Windows;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace MjStudio.Host.ViewModels
{
    /// <summary>
    /// 项目页 ViewModel：卡片网格 + 右侧抽屉编辑
    /// </summary>
    public class ProjectsViewModel : ViewModelBase
    {
        private readonly IServiceProvider _services;
        private readonly IProjectService _projects;
        private readonly CurrentProject _current;

        // ===== 项目卡片列表 =====
        public ObservableCollection<Project> ProjectCards { get; } = new();

        /// <summary>搜索过滤后的卡片（绑定到 UI）</summary>
        public ObservableCollection<Project> FilteredCards { get; } = new();

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplyFilter();
            }
        }

        private string _currentProjectName = "";
        public string CurrentProjectName { get => _currentProjectName; set => SetProperty(ref _currentProjectName, value); }

        // ===== 当前编辑的项目（抽屉内） =====
        private Project? _selectedProject;
        public Project? SelectedProject { get => _selectedProject; private set => SetProperty(ref _selectedProject, value); }

        // ===== 新建项目 =====
        private string _newProjectName = "";
        public string NewProjectName { get => _newProjectName; set => SetProperty(ref _newProjectName, value); }

        // ===== 编辑表单字段 =====
        private string _formStoryName = "";
        public string FormStoryName { get => _formStoryName; set => SetProperty(ref _formStoryName, value); }
        private string _formWorldview = "";
        public string FormWorldview { get => _formWorldview; set => SetProperty(ref _formWorldview, value); }
        private string _formDescription = "";
        public string FormDescription { get => _formDescription; set => SetProperty(ref _formDescription, value); }
        private string _formTotalEpisodes = "";
        public string FormTotalEpisodes { get => _formTotalEpisodes; set => SetProperty(ref _formTotalEpisodes, value); }
        private string _formEpisodeDuration = "";
        public string FormEpisodeDuration { get => _formEpisodeDuration; set => SetProperty(ref _formEpisodeDuration, value); }
        private AspectRatioEnum _formAspectRatio = AspectRatioEnum.Portrait916;
        public AspectRatioEnum FormAspectRatio { get => _formAspectRatio; set => SetProperty(ref _formAspectRatio, value); }
        public AspectRatioEnum[] AspectRatios { get; } = Enum.GetValues<AspectRatioEnum>();
        private string _formTargetPlatform = "抖音";
        public string FormTargetPlatform { get => _formTargetPlatform; set => SetProperty(ref _formTargetPlatform, value); }
        public string[] PlatformOptions { get; } = { "抖音", "快手", "B站", "视频号", "YouTube", "其他" };
        private string _formGenre = "都市";
        public string FormGenre { get => _formGenre; set => SetProperty(ref _formGenre, value); }
        public string[] GenreOptions { get; } = { "都市", "玄幻", "仙侠", "科幻", "悬疑", "甜宠", "逆袭", "其他" };
        private string _formAudience = "成人";
        public string FormAudience { get => _formAudience; set => SetProperty(ref _formAudience, value); }
        public string[] AudienceOptions { get; } = { "成人", "青少年", "儿童", "全年龄" };
        private string _formArtStyle = "真人电影";
        public string FormArtStyle { get => _formArtStyle; set => SetProperty(ref _formArtStyle, value); }
        public string[] ArtStyleOptions { get; } = { "真人电影", "国漫", "日漫", "3D", "水墨", "像素", "其他" };
        private string _formVoiceLanguage = "中文普通话";
        public string FormVoiceLanguage { get => _formVoiceLanguage; set => SetProperty(ref _formVoiceLanguage, value); }
        public string[] VoiceLanguageOptions { get; } = { "中文普通话", "粤语", "英语", "日语", "韩语", "其他" };
        private string _formOriginalOrAdapted = "原创";
        public string FormOriginalOrAdapted { get => _formOriginalOrAdapted; set => SetProperty(ref _formOriginalOrAdapted, value); }
        public string[] OriginalOrAdaptedOptions { get; } = { "原创", "改编" };
        private string _formBgmStyle = "";
        public string FormBgmStyle { get => _formBgmStyle; set => SetProperty(ref _formBgmStyle, value); }
        private bool _formHasOpeningEnding;
        public bool FormHasOpeningEnding { get => _formHasOpeningEnding; set => SetProperty(ref _formHasOpeningEnding, value); }
        private ProjectStatusEnum _formStatus = ProjectStatusEnum.InProgress;
        public ProjectStatusEnum FormStatus { get => _formStatus; set => SetProperty(ref _formStatus, value); }
        public ProjectStatusEnum[] StatusOptions { get; } = Enum.GetValues<ProjectStatusEnum>();

        // ===== 命令 =====
        public RelayCommand RefreshCommand { get; }
        public RelayCommand SearchCommand { get; }
        public RelayCommand CreateCommand { get; }
        public RelayCommand ConfirmCreateCommand { get; }
        public RelayCommand CancelCreateCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand OpenDrawerCommand { get; }
        public RelayCommand CloseDrawerCommand { get; }
        public RelayCommand OpenShotsCommand { get; }
        public RelayCommand OpenStoryCommand { get; }

        /// <summary>项目发生变动（新建/切换/删除）时触发，参数为当前项目名（可能为 null）</summary>
        public event Action<string?>? ProjectChanged;

        /// <summary>请求导航到指定页面（漫剧创作→分镜、剧本创作→剧本）</summary>
        public event Action<string>? NavigateRequested;

        public ProjectsViewModel(IServiceProvider services)
        {
            _services = services;
            _projects = services.GetRequiredService<IProjectService>();
            _current = services.GetRequiredService<CurrentProject>();

            RefreshCommand = new RelayCommand(Refresh);
            SearchCommand = new RelayCommand(_ => ApplyFilter());
            CreateCommand = new RelayCommand(OpenCreateDialog);
            ConfirmCreateCommand = new RelayCommand(Create);
            CancelCreateCommand = new RelayCommand(CancelCreate);
            SaveCommand = new RelayCommand(Save);
            DeleteCommand = new RelayCommand(Delete);
            OpenDrawerCommand = new RelayCommand(OpenDrawer);
            CloseDrawerCommand = new RelayCommand(CloseDrawer);
            OpenShotsCommand = new RelayCommand(OpenShots);
            OpenStoryCommand = new RelayCommand(OpenStory);

            Refresh();
        }

        /// <summary>按搜索词过滤卡片（名称 / 故事名）</summary>
        private void ApplyFilter()
        {
            FilteredCards.Clear();
            var keyword = SearchText?.Trim();
            foreach (var p in ProjectCards)
            {
                if (string.IsNullOrEmpty(keyword)) { FilteredCards.Add(p); continue; }
                if ((p.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (p.StoryName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                    FilteredCards.Add(p);
            }
        }

        /// <summary>刷新项目卡片列表</summary>
        public void Refresh()
        {
            ProjectCards.Clear();
            try
            {
                var names = _projects.ListProjectNamesAsync().GetAwaiter().GetResult();
                foreach (var name in names)
                {
                    var project = _projects.GetByNameAsync(name).GetAwaiter().GetResult();
                    if (project is not null) ProjectCards.Add(project);
                }
            }
            catch { /* 忽略 */ }

            CurrentProjectName = _current.Name ?? "";
            ApplyFilter();
        }

        /// <summary>打开新建项目弹窗（由视图层控制动画）</summary>
        public void OpenCreateDialog()
        {
            NewProjectName = "";
            View?.ShowCreateDialog();
        }

        /// <summary>取消新建</summary>
        public void CancelCreate()
        {
            View?.HideCreateDialog();
        }

        /// <summary>视图引用（由视图层注入，用于控制弹窗/抽屉动画）</summary>
        public Views.ProjectsView? View { get; set; }

        /// <summary>打开抽屉并加载项目表单</summary>
        private void OpenDrawer(object? parameter)
        {
            if (parameter is not Project project) return;
            try
            {
                _current.Set(project.Name);
                CurrentProjectName = project.Name;
                SelectedProject = project;
                LoadForm(project);
                View?.ShowDrawer();
                ProjectChanged?.Invoke(project.Name);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开项目失败：{ex.Message}", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>卡片「漫剧创作」：加载项目并进入分镜页面</summary>
        private void OpenShots(object? parameter)
        {
            if (parameter is not Project project) return;
            _current.Set(project.Name);
            CurrentProjectName = project.Name;
            ProjectChanged?.Invoke(project.Name);
            NavigateRequested?.Invoke("shots");
        }

        /// <summary>卡片「剧本创作」：加载项目并进入剧本页面</summary>
        private void OpenStory(object? parameter)
        {
            if (parameter is not Project project) return;
            _current.Set(project.Name);
            CurrentProjectName = project.Name;
            ProjectChanged?.Invoke(project.Name);
            NavigateRequested?.Invoke("story");
        }

        /// <summary>关闭抽屉</summary>
        private void CloseDrawer()
        {
            View?.HideDrawer();
        }

        private void LoadForm(Project p)
        {
            FormStoryName = p.StoryName ?? "";
            FormWorldview = p.Worldview ?? "";
            FormDescription = p.Description ?? "";
            FormTotalEpisodes = p.TotalEpisodes.ToString();
            FormEpisodeDuration = p.EpisodeDuration.ToString();
            FormAspectRatio = p.AspectRatio;
            FormTargetPlatform = p.TargetPlatform ?? "";
            FormGenre = p.Genre ?? "";
            FormAudience = p.Audience ?? "";
            FormArtStyle = p.ArtStyle ?? "";
            FormVoiceLanguage = p.VoiceLanguage ?? "";
            FormOriginalOrAdapted = string.IsNullOrWhiteSpace(p.OriginalOrAdapted) ? "原创" : p.OriginalOrAdapted;
            FormBgmStyle = p.BgmStyle ?? "";
            FormHasOpeningEnding = p.HasOpeningEnding;
            FormStatus = p.Status;
        }

        private void ClearForm()
        {
            FormStoryName = ""; FormWorldview = ""; FormDescription = "";
            FormTotalEpisodes = "20"; FormEpisodeDuration = "120";
            FormAspectRatio = AspectRatioEnum.Landscape169;
            FormTargetPlatform = "抖音"; FormGenre = "都市"; FormAudience = "成人";
            FormArtStyle = "真人电影"; FormVoiceLanguage = "中文普通话";
            FormOriginalOrAdapted = "原创"; FormBgmStyle = "";
            FormHasOpeningEnding = false;
            FormStatus = ProjectStatusEnum.InProgress;
        }

        /// <summary>确认新建项目（弹窗内点「创建」）</summary>
        private void Create()
        {
            if (string.IsNullOrWhiteSpace(NewProjectName))
            {
                View?.FocusNewNameBox();
                return;
            }
            var name = NewProjectName.Trim();
            if (ProjectCards.Any(p => p.Name == name))
            {
                MessageBox.Show($"项目「{name}」已存在", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                var project = new Project
                {
                    Name = name,
                    StoryName = name,
                    TotalEpisodes = 20,
                    EpisodeDuration = 120,
                    AspectRatio = AspectRatioEnum.Landscape169,
                    TargetPlatform = "抖音",
                    Genre = "都市",
                    Audience = "成人",
                    ArtStyle = "真人电影",
                    VoiceLanguage = "中文普通话",
                    OriginalOrAdapted = "原创"
                };
                _projects.CreateAsync(project).GetAwaiter().GetResult();
                NewProjectName = "";
                Refresh();
                View?.HideCreateDialog();
                // 自动打开新建项目的抽屉
                var created = ProjectCards.FirstOrDefault(p => p.Name == name);
                if (created is not null) OpenDrawer(created);
            }
            catch (Exception ex)
            {
                MessageBox.Show("创建项目失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>保存编辑</summary>
        private void Save()
        {
            if (SelectedProject is null) return;
            try
            {
                if (_current.Name != SelectedProject.Name)
                    _current.Set(SelectedProject.Name);

                SelectedProject.StoryName = FormStoryName;
                SelectedProject.Worldview = FormWorldview;
                SelectedProject.Description = FormDescription;
                if (int.TryParse(FormTotalEpisodes, out var te)) SelectedProject.TotalEpisodes = te;
                if (int.TryParse(FormEpisodeDuration, out var ed)) SelectedProject.EpisodeDuration = ed;
                SelectedProject.AspectRatio = FormAspectRatio;
                SelectedProject.TargetPlatform = FormTargetPlatform;
                SelectedProject.Genre = FormGenre;
                SelectedProject.Audience = FormAudience;
                SelectedProject.ArtStyle = FormArtStyle;
                SelectedProject.VoiceLanguage = FormVoiceLanguage;
                SelectedProject.OriginalOrAdapted = FormOriginalOrAdapted;
                SelectedProject.BgmStyle = FormBgmStyle;
                SelectedProject.HasOpeningEnding = FormHasOpeningEnding;
                SelectedProject.Status = FormStatus;

                _projects.UpdateAsync(SelectedProject).GetAwaiter().GetResult();
                Refresh();
                MessageBox.Show("项目信息已保存", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>删除项目</summary>
        private void Delete()
        {
            if (SelectedProject is null) return;
            if (MessageBox.Show(
                $"确定删除项目「{SelectedProject.Name}」？\n\n将删除项目数据库与全部资源文件，此操作不可恢复！",
                "MjStudio", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            try
            {
                var deletedName = SelectedProject.Name;
                _projects.DeleteAsync(deletedName).GetAwaiter().GetResult();
                View?.HideDrawer();
                SelectedProject = null;
                ClearForm();
                Refresh();
                ProjectChanged?.Invoke(_current.Name);
                MessageBox.Show("项目已删除", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
