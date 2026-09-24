using System.Net.Http;
using System.Windows;
using MjStudio.Application.Shared.Services;
using MjStudio.Host.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MjStudio.Host.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel：项目选择/加载、内置 API 状态、ComfyUI 健康检查、导航、设置。
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly IServiceProvider _services;
        private readonly ApiServerManager _apiServer;
        private readonly ISettingsService _settings;
        private readonly IProjectService _projects;
        private readonly CurrentProject _current;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
        private readonly System.Threading.Timer? _comfyTimer;

        // ===== 当前项目（只读展示，切换在项目页进行） =====
        private bool _isProjectLoaded;
        public bool IsProjectLoaded { get => _isProjectLoaded; set => SetProperty(ref _isProjectLoaded, value); }

        private string _currentProjectName = "";
        public string CurrentProjectName { get => _currentProjectName; set => SetProperty(ref _currentProjectName, value); }

        // ===== API 状态 =====
        private bool _apiRunning;
        public bool ApiRunning { get => _apiRunning; set => SetProperty(ref _apiRunning, value); }

        private string _apiUrl = "";
        public string ApiUrl { get => _apiUrl; set => SetProperty(ref _apiUrl, value); }

        // ===== ComfyUI 状态 =====
        private bool _comfyHealthy;
        public bool ComfyHealthy { get => _comfyHealthy; set => SetProperty(ref _comfyHealthy, value); }

        private string _comfyStatusText = "检测中…";
        public string ComfyStatusText { get => _comfyStatusText; set => SetProperty(ref _comfyStatusText, value); }

        // ===== 设置 =====
        private int _apiPort;
        public int ApiPort { get => _apiPort; set => SetProperty(ref _apiPort, value); }

        private string _comfyUiBaseUrl = "";
        public string ComfyUiBaseUrl { get => _comfyUiBaseUrl; set => SetProperty(ref _comfyUiBaseUrl, value); }

        private string _comfyUiLaunchPath = "";
        public string ComfyUiLaunchPath { get => _comfyUiLaunchPath; set => SetProperty(ref _comfyUiLaunchPath, value); }

        private string _dbRoot = "";
        public string DbRoot { get => _dbRoot; set => SetProperty(ref _dbRoot, value); }

        private string _resourcesRoot = "";
        public string ResourcesRoot { get => _resourcesRoot; set => SetProperty(ref _resourcesRoot, value); }

        // ===== 侧边栏收起/展开 =====
        private bool _sidebarCollapsed;
        public bool SidebarCollapsed { get => _sidebarCollapsed; set => SetProperty(ref _sidebarCollapsed, value); }

        /// <summary>侧边栏宽度（展开 220 / 收起 0）</summary>
        public double SidebarWidth => SidebarCollapsed ? 0 : 220;

        // ===== 导航 =====
        private string _currentPage = "projects";
        public string CurrentPage { get => _currentPage; set => SetProperty(ref _currentPage, value); }

        private string _pageTitle = "项目";
        public string PageTitle { get => _pageTitle; set => SetProperty(ref _pageTitle, value); }

        private bool _showProjects = true;
        public bool ShowProjects { get => _showProjects; set => SetProperty(ref _showProjects, value); }
        private bool _showAssets;
        public bool ShowAssets { get => _showAssets; set => SetProperty(ref _showAssets, value); }
        private bool _showShots;
        public bool ShowShots { get => _showShots; set => SetProperty(ref _showShots, value); }
        private bool _showStory;
        public bool ShowStory { get => _showStory; set => SetProperty(ref _showStory, value); }
        private bool _showWorkflow;
        public bool ShowWorkflow { get => _showWorkflow; set => SetProperty(ref _showWorkflow, value); }
        private bool _showSettings;
        public bool ShowSettings { get => _showSettings; set => SetProperty(ref _showSettings, value); }

        // 页面视图
        public object? ProjectsPage { get; }
        public object? AssetsPage { get; }
        public object? ShotsPage { get; }
        public object? StoryPage { get; }
        public object? WorkflowPage { get; }
        public object? SettingsPage { get; }

        public RelayCommand NavigateCommand { get; }
        public RelayCommand ToggleSidebarCommand { get; }
        public RelayCommand SaveSettingsCommand { get; }
        public RelayCommand RestartApiCommand { get; }
        public RelayCommand LaunchComfyUiCommand { get; }

        /// <summary>项目变动回调（同步顶栏 + 刷新数据页），由构造函数初始化</summary>
        private Action _onProjectChanged = () => { };

        public MainViewModel(IServiceProvider services, ApiServerManager apiServer)
        {
            _services = services;
            _apiServer = apiServer;
            _settings = services.GetRequiredService<ISettingsService>();
            _projects = services.GetRequiredService<IProjectService>();
            _current = services.GetRequiredService<CurrentProject>();

            // 页面视图
            var projectsVm = new ProjectsViewModel(services);
            var assetsVm = new AssetsViewModel(services);
            var shotsVm = new ShotsViewModel(services);
            var storyVm = new StoryViewModel(services);

            // 项目变动（新建/切换/删除）→ 同步顶栏 + 刷新所有数据页
            _onProjectChanged = () =>
            {
                CurrentProjectName = _current.Name ?? "";
                IsProjectLoaded = _current.IsLoaded;
                assetsVm.Refresh();
                shotsVm.Refresh();
                storyVm.Refresh();
            };
            projectsVm.ProjectChanged += _ => _onProjectChanged();
            // 项目卡片「漫剧创作/剧本创作」→ 导航到分镜/剧本页面
            projectsVm.NavigateRequested += page => Navigate(page);

            ProjectsPage = new Views.ProjectsView { DataContext = projectsVm };
            AssetsPage = new Views.AssetsView { DataContext = assetsVm };
            ShotsPage = new Views.ShotsView { DataContext = shotsVm };
            StoryPage = new Views.StoryView { DataContext = storyVm };
            WorkflowPage = new Views.WorkflowView { DataContext = new WorkflowViewModel(services) };
            SettingsPage = new Views.SettingsView { DataContext = this };

            NavigateCommand = new RelayCommand(Navigate);
            ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            RestartApiCommand = new RelayCommand(RestartApi);
            LaunchComfyUiCommand = new RelayCommand(LaunchComfyUi);

            // 初始化
            var opts = _settings.Get();
            ApiPort = opts.ApiPort;
            ComfyUiBaseUrl = opts.ComfyUiBaseUrl;
            ComfyUiLaunchPath = opts.ComfyUiLaunchPath;
            DbRoot = opts.DbRoot;
            ResourcesRoot = opts.ResourcesRoot;
            UpdateApiStatus();
            _onProjectChanged();

            // ComfyUI 健康检查：立即一次 + 每 30 秒一次
            _comfyTimer = new System.Threading.Timer(_ => CheckComfyHealth(), null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        }

        private void UpdateApiStatus()
        {
            ApiRunning = _apiServer.IsRunning;
            ApiUrl = _apiServer.Url ?? "";
        }

        /// <summary>ComfyUI 健康检查：GET /system_stats，成功即健康</summary>
        private async void CheckComfyHealth()
        {
            try
            {
                var baseUrl = ComfyUiBaseUrl.TrimEnd('/');
                if (string.IsNullOrEmpty(baseUrl))
                {
                    ComfyHealthy = false;
                    ComfyStatusText = "未配置地址";
                    return;
                }
                using var resp = await _http.GetAsync($"{baseUrl}/system_stats");
                ComfyHealthy = resp.IsSuccessStatusCode;
                ComfyStatusText = ComfyHealthy ? "健康" : "不可用";
            }
            catch
            {
                ComfyHealthy = false;
                ComfyStatusText = "不可用";
            }
        }

        /// <summary>启动 ComfyUI 程序（若配置了启动地址且未运行）</summary>
        private void LaunchComfyUi()
        {
            if (string.IsNullOrWhiteSpace(ComfyUiLaunchPath))
            {
                MessageBox.Show("请先在设置中配置 ComfyUI 程序启动地址。", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!System.IO.File.Exists(ComfyUiLaunchPath))
            {
                MessageBox.Show($"找不到 ComfyUI 程序：{ComfyUiLaunchPath}", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ComfyUiLaunchPath,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(ComfyUiLaunchPath) ?? "",
                    UseShellExecute = true
                });
                MessageBox.Show("已启动 ComfyUI，等待其就绪后顶部状态会变为「健康」。", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动 ComfyUI 失败：{ex.Message}", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveSettings()
        {
            var opts = _settings.Get();
            opts.ApiPort = ApiPort;
            opts.ComfyUiBaseUrl = ComfyUiBaseUrl;
            opts.ComfyUiLaunchPath = ComfyUiLaunchPath;
            opts.DbRoot = DbRoot;
            opts.ResourcesRoot = ResourcesRoot;
            _settings.Update(opts);
            MessageBox.Show("设置已保存。若修改了端口，请点击「重启 API」生效。", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RestartApi()
        {
            var opts = _settings.Get();
            _apiServer.Restart(opts);
            UpdateApiStatus();
            MessageBox.Show($"API 已重启：{ApiUrl}", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ToggleSidebar()
        {
            SidebarCollapsed = !SidebarCollapsed;
            OnPropertyChanged(nameof(SidebarWidth));
        }

        private void Navigate(object? parameter)
        {
            if (parameter is not string page) return;
            CurrentPage = page;
            ShowProjects = page == "projects";
            ShowAssets = page == "assets";
            ShowShots = page == "shots";
            ShowStory = page == "story";
            ShowWorkflow = page == "workflow";
            ShowSettings = page == "settings";
            PageTitle = page switch
            {
                "projects" => "项目",
                "assets" => "资产",
                "shots" => "分镜",
                "story" => "剧本",
                "workflow" => "工作流",
                "settings" => "设置",
                _ => "项目"
            };

            // 进入数据页（资产/分镜/剧本）时若未加载项目，自动加载第一个项目
            if (!_current.IsLoaded && page is "assets" or "shots" or "story")
            {
                try
                {
                    var names = _projects.ListProjectNamesAsync().GetAwaiter().GetResult();
                    if (names.Count > 0)
                    {
                        _current.Set(names[0]);
                        _onProjectChanged();
                    }
                }
                catch { /* 忽略 */ }
            }
        }
    }
}
