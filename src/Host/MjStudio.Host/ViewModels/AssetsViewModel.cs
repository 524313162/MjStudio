using System.Collections.ObjectModel;
using System.Windows;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using MjStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MjStudio.Host.ViewModels
{
    /// <summary>
    /// 资产页 ViewModel：按类型展示/新建/编辑/删除资产（名称 + 描述 + 单个正向/反向提示词 + 子资产 + 引用项目）
    /// </summary>
    public class AssetsViewModel : ViewModelBase
    {
        private readonly IServiceProvider _services;
        private readonly IAssetService _assets;
        private readonly IProjectService _projects;
        private readonly IResourceService _resources;
        private readonly ResourceStorageService _resourceStorage;
        private readonly CurrentProject _current;

        // ===== 资产卡片列表 =====
        public ObservableCollection<AssetCardItem> Cards { get; } = new();

        /// <summary>搜索/类型过滤后的卡片（绑定到 UI）</summary>
        public ObservableCollection<AssetCardItem> FilteredCards { get; } = new();

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

        /// <summary>类型过滤（null=全部）</summary>
        private AssetTypeEnum? _filterType;
        public AssetTypeEnum? FilterType
        {
            get => _filterType;
            set
            {
                if (SetProperty(ref _filterType, value))
                    ApplyFilter();
            }
        }

        public AssetTypeEnum[] Types { get; } = Enum.GetValues<AssetTypeEnum>();

        /// <summary>类型过滤下拉选项（首项 null=全部）</summary>
        public AssetTypeEnum?[] TypeFilterOptions { get; } = BuildTypeFilterOptions();

        private static AssetTypeEnum?[] BuildTypeFilterOptions()
        {
            var values = Enum.GetValues<AssetTypeEnum>();
            var result = new AssetTypeEnum?[values.Length + 1];
            result[0] = null;
            for (int i = 0; i < values.Length; i++) result[i + 1] = values[i];
            return result;
        }

        // ===== 当前编辑的资产（抽屉内）=====
        private Asset? _selected;
        public Asset? Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    if (value is not null) LoadFormFromSelected();
                }
            }
        }

        /// <summary>当前编辑的卡片项（含 FullDescription 等展示属性）</summary>
        private AssetCardItem? _selectedCard;
        public AssetCardItem? SelectedCard
        {
            get => _selectedCard;
            set => SetProperty(ref _selectedCard, value);
        }

        /// <summary>正在创建的子资产的父资产（null=顶层资产）</summary>
        private Asset? _parentAsset;
        public Asset? ParentAsset
        {
            get => _parentAsset;
            set => SetProperty(ref _parentAsset, value);
        }

        /// <summary>是否正在创建子资产（类型锁定为父资产类型）</summary>
        public bool IsCreatingChild => !IsEditing && ParentAsset is not null;

        /// <summary>抽屉标题（编辑 / 新建 / 新建子资产）</summary>
        public string DrawerTitle => IsEditing ? "编辑资产" : (IsCreatingChild ? "新建子资产" : "新建资产");

        /// <summary>抽屉副标题（子资产显示父资产名）</summary>
        public string DrawerSubtitle => IsCreatingChild ? $"父资产：{ParentAsset!.Name}" : (Selected?.Name ?? "新资产");

        /// <summary>类型是否可编辑（子资产也可选择类型：角色的声音=音频、变装=角色、场景子面=场景）</summary>
        public bool CanEditType => true;

        /// <summary>表单实时预览：组合当前表单字段为「全部描述」文本</summary>
        public string PreviewDescription => BuildFullDescriptionFromForm();

        // ===== 表单字段 =====
        private string _formName = "";
        public string FormName { get => _formName; set => SetProperty(ref _formName, value); }

        private string _formDescription = "";
        public string FormDescription { get => _formDescription; set => SetProperty(ref _formDescription, value); }

        /// <summary>表单中的资产类型（新建时显式选择，不再默认跟随过滤）</summary>
        private AssetTypeEnum _formAssetType = AssetTypeEnum.Character;
        public AssetTypeEnum FormAssetType
        {
            get => _formAssetType;
            set => SetProperty(ref _formAssetType, value);
        }

        // 提示词（单个正向 + 单个反向，中英文均可）
        private string _formPrompt = "";
        public string FormPrompt { get => _formPrompt; set => SetProperty(ref _formPrompt, value); }

        private string _formNegativePrompt = "";
        public string FormNegativePrompt { get => _formNegativePrompt; set => SetProperty(ref _formNegativePrompt, value); }

        // ===== 引用项目（多对多）=====
        public ObservableCollection<ProjectRefItem> ProjectRefItems { get; } = new();

        // ===== 状态 =====
        private bool _isEditing;
        public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }

        // ===== 命令 =====
        public RelayCommand RefreshCommand { get; }
        public RelayCommand SearchCommand { get; }
        public RelayCommand CreateCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand OpenDrawerCommand { get; }
        public RelayCommand CloseDrawerCommand { get; }
        public RelayCommand CreateChildCommand { get; }

        /// <summary>视图引用（由视图层注入，用于控制抽屉动画）</summary>
        public Views.AssetsView? View { get; set; }

        public AssetsViewModel(IServiceProvider services)
        {
            _services = services;
            _assets = services.GetRequiredService<IAssetService>();
            _projects = services.GetRequiredService<IProjectService>();
            _resources = services.GetRequiredService<IResourceService>();
            _resourceStorage = services.GetRequiredService<ResourceStorageService>();
            _current = services.GetRequiredService<CurrentProject>();

            RefreshCommand = new RelayCommand(Refresh);
            SearchCommand = new RelayCommand(_ => ApplyFilter());
            CreateCommand = new RelayCommand(_ => StartCreate());
            SaveCommand = new RelayCommand(Save);
            DeleteCommand = new RelayCommand(Delete);
            OpenDrawerCommand = new RelayCommand(OpenDrawer);
            CloseDrawerCommand = new RelayCommand(CloseDrawer);
            CreateChildCommand = new RelayCommand(CreateChild);

            // 表单字段 / 编辑状态变化时，刷新实时预览与抽屉标题
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is null
                    || e.PropertyName.StartsWith("Form", StringComparison.Ordinal)
                    || e.PropertyName is nameof(IsEditing) or nameof(ParentAsset) or nameof(FormAssetType))
                {
                    OnPropertyChanged(nameof(PreviewDescription));
                    OnPropertyChanged(nameof(DrawerTitle));
                    OnPropertyChanged(nameof(DrawerSubtitle));
                    OnPropertyChanged(nameof(CanEditType));
                    OnPropertyChanged(nameof(IsCreatingChild));
                }
            };

            Refresh();
        }

        /// <summary>组合当前表单字段为「全部描述」文本（与 AssetCardItem.BuildFullDescription 同构）</summary>
        private string BuildFullDescriptionFromForm()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(FormDescription)) parts.Add(FormDescription.Trim());
            if (!string.IsNullOrWhiteSpace(FormPrompt)) parts.Add($"提示词：{FormPrompt.Trim()}");
            if (!string.IsNullOrWhiteSpace(FormNegativePrompt)) parts.Add($"反向：{FormNegativePrompt.Trim()}");
            return parts.Count == 0 ? "（填写上方字段后，此处实时显示资产的全部描述 / 提示词）" : string.Join("\n", parts);
        }

        /// <summary>引用项目复选项</summary>
        public class ProjectRefItem
        {
            public long Id { get; set; }
            public string Name { get; set; } = "";
            public bool IsChecked { get; set; }
        }

        /// <summary>按搜索词 + 类型过滤卡片</summary>
        private void ApplyFilter()
        {
            FilteredCards.Clear();
            var keyword = SearchText?.Trim();
            foreach (var c in Cards)
            {
                if (FilterType is not null && c.Asset.AssetType != FilterType.Value) continue;
                if (!string.IsNullOrEmpty(keyword))
                {
                    var hit = (c.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (c.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (c.FullDescription?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false);
                    if (!hit) continue;
                }
                FilteredCards.Add(c);
            }
        }

        /// <summary>刷新全部资产（跨项目，含引用项目、子资产、资源路径）</summary>
        public void Refresh()
        {
            Cards.Clear();
            try
            {
                var projectNames = _projects.ListProjectNamesAsync().GetAwaiter().GetResult();
                var list = _assets.GetAllWithRefsAsync().GetAwaiter().GetResult();
                foreach (var a in list)
                {
                    // 子资产（角色声音/变装、场景子面）只在父资产卡片内展示，不重复出现在顶层
                    if (a.ParentAssetId is not null) continue;
                    Cards.Add(new AssetCardItem(a, _resourceStorage, projectNames));
                }
            }
            catch { /* 忽略 */ }
            ApplyFilter();
        }

        /// <summary>打开新建抽屉（parent 非 null 时创建子资产）</summary>
        private void StartCreate(Asset? parent = null)
        {
            ClearForm();
            IsEditing = false;
            Selected = null;
            SelectedCard = null;
            ParentAsset = parent;
            if (parent is not null)
            {
                // 子资产默认跟随父资产类型，但可修改（角色的声音=音频、变装=角色、场景子面=场景）
                FormAssetType = parent.AssetType;
                FormName = parent.Name + "-";
            }
            else
            {
                // 新建时类型跟随顶部过滤选择（选了类型则默认该类型，否则默认角色）
                FormAssetType = FilterType ?? AssetTypeEnum.Character;
            }
            // 子资产继承父资产的引用项目；顶层资产默认勾选当前项目
            LoadProjectRefItems(parent);
            View?.ShowDrawer();
        }

        /// <summary>为当前编辑的资产新建子资产</summary>
        private void CreateChild()
        {
            if (Selected is null) return;
            StartCreate(Selected);
        }

        /// <summary>打开抽屉并加载资产表单</summary>
        private void OpenDrawer(object? parameter)
        {
            if (parameter is not AssetCardItem card) return;
            try
            {
                SelectedCard = card;
                Selected = card.Asset;
                FormAssetType = card.Asset.AssetType;
                LoadProjectRefItems(card.Asset);
                View?.ShowDrawer();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开资产失败：{ex.Message}", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>关闭抽屉</summary>
        private void CloseDrawer()
        {
            View?.HideDrawer();
        }

        /// <summary>加载「引用项目」复选项（asset 为 null 时默认勾选当前项目）</summary>
        private void LoadProjectRefItems(Asset? asset)
        {
            ProjectRefItems.Clear();
            var checkedIds = new HashSet<long>(
                asset?.ProjectRefs.Select(r => r.ProjectId) ?? Enumerable.Empty<long>());
            if (asset is null && _current.IsLoaded)
            {
                try
                {
                    var pid = _resources.GetProjectIdAsync().GetAwaiter().GetResult();
                    checkedIds.Add(pid);
                }
                catch { /* 忽略 */ }
            }

            try
            {
                var names = _projects.ListProjectNamesAsync().GetAwaiter().GetResult();
                foreach (var name in names)
                {
                    var p = _projects.GetByNameAsync(name).GetAwaiter().GetResult();
                    if (p is null) continue;
                    ProjectRefItems.Add(new ProjectRefItem
                    {
                        Id = p.Id,
                        Name = p.Name,
                        IsChecked = checkedIds.Contains(p.Id)
                    });
                }
            }
            catch { /* 忽略 */ }
        }

        private void LoadFormFromSelected()
        {
            if (Selected is null) { ClearForm(); return; }
            IsEditing = true;
            FormName = Selected.Name ?? "";
            FormDescription = Selected.Description ?? "";
            FormAssetType = Selected.AssetType;
            FormPrompt = Selected.Prompt ?? "";
            FormNegativePrompt = Selected.NegativePrompt ?? "";
        }

        private void ClearForm()
        {
            IsEditing = false;
            ParentAsset = null;
            FormName = ""; FormDescription = "";
            FormAssetType = AssetTypeEnum.Character;
            FormPrompt = ""; FormNegativePrompt = "";
        }

        private Asset BuildAssetFromForm()
        {
            return new Asset
            {
                AssetType = FormAssetType,
                Name = FormName.Trim(),
                Description = FormDescription,
                Prompt = FormPrompt,
                NegativePrompt = FormNegativePrompt
            };
        }

        private void Save()
        {
            if (string.IsNullOrWhiteSpace(FormName))
            {
                MessageBox.Show("请输入资产名称", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                var asset = BuildAssetFromForm();
                long assetId;
                if (IsEditing && Selected is not null)
                {
                    asset.Id = Selected.Id;
                    asset.CreatedTime = Selected.CreatedTime;
                    asset.ProjectId = Selected.ProjectId;
                    asset.ResourceId = Selected.ResourceId;
                    asset.ParentAssetId = Selected.ParentAssetId;
                    asset.Order = Selected.Order;
                    _assets.UpdateAsync(asset).GetAwaiter().GetResult();
                    assetId = asset.Id;
                }
                else
                {
                    if (_current.IsLoaded)
                    {
                        try { asset.ProjectId = _resources.GetProjectIdAsync().GetAwaiter().GetResult(); }
                        catch { /* 忽略 */ }
                    }
                    // 子资产：挂到父资产下
                    if (ParentAsset is not null)
                        asset.ParentAssetId = ParentAsset.Id;
                    var created = _assets.CreateAsync(asset).GetAwaiter().GetResult();
                    assetId = created.Id;
                }

                // 保存引用项目（多对多）
                var refIds = ProjectRefItems.Where(x => x.IsChecked).Select(x => x.Id).ToList();
                _assets.SetProjectRefsAsync(assetId, refIds).GetAwaiter().GetResult();

                Refresh();
                MessageBox.Show("资产已保存", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Delete()
        {
            if (Selected is null) return;
            if (MessageBox.Show($"确定删除资产「{Selected.Name}」？", "MjStudio", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try
            {
                _assets.DeleteAsync(Selected.Id).GetAwaiter().GetResult();
                View?.HideDrawer();
                Selected = null;
                ClearForm();
                Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
