using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using MjStudio.Host.ViewModels;
using MjStudio.Infrastructure;

namespace MjStudio.Host.Views
{
    /// <summary>
    /// 绑定角色资产对话框：从现有角色资产中多选，确定后返回选中的资产。
    /// </summary>
    public partial class BindAssetDialog : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        /// <summary>对话框内可勾选的资产项</summary>
        public class BindAssetItem : ViewModelBase
        {
            private bool _isChecked;

            public Asset Asset { get; }
            public long AssetId => Asset.Id;
            public string Name => Asset.Name ?? "";
            public string Description => Asset.Description ?? "";
            public string TypeDisplay { get; }
            public string? ImagePath { get; }
            public bool HasImage => !string.IsNullOrEmpty(ImagePath) && System.IO.File.Exists(ImagePath);

            public bool IsChecked
            {
                get => _isChecked;
                set => SetProperty(ref _isChecked, value);
            }

            public BindAssetItem(Asset asset, ResourceStorageService resources, IReadOnlyList<string> projectNames)
            {
                Asset = asset;
                TypeDisplay = GetDisplayName(asset.AssetType);
                ImagePath = ResolveImage(asset.Resource, resources, projectNames);
            }

            private static string GetDisplayName(Enum e)
            {
                var field = e.GetType().GetField(e.ToString());
                var attr = field?.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false)
                    .FirstOrDefault() as System.ComponentModel.DataAnnotations.DisplayAttribute;
                return attr?.Name ?? e.ToString();
            }

            private static string? ResolveImage(Resource? resource, ResourceStorageService resources, IReadOnlyList<string> projectNames)
            {
                if (resource is null || string.IsNullOrEmpty(resource.RelativePath)) return null;
                foreach (var name in projectNames)
                {
                    try
                    {
                        var abs = resources.GetAbsolutePath(name, resource.RelativePath);
                        if (System.IO.File.Exists(abs)) return abs;
                    }
                    catch { /* 忽略 */ }
                }
                return null;
            }
        }

        public ObservableCollection<BindAssetItem> Assets { get; } = new();

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

        private int _filteredCount;
        public int FilteredCount
        {
            get => _filteredCount;
            private set => SetProperty(ref _filteredCount, value);
        }

        private ICollectionView _view;

        /// <summary>确定后返回选中的资产</summary>
        public List<Asset>? SelectedAssets { get; private set; }

        public BindAssetDialog(List<Asset> available, ResourceStorageService resources, IReadOnlyList<string> projectNames)
        {
            InitializeComponent();
            foreach (var a in available)
                Assets.Add(new BindAssetItem(a, resources, projectNames));

            // 用 CollectionView 做过滤
            _view = CollectionViewSource.GetDefaultView(Assets);
            _view.Filter = FilterPredicate;
            AssetList.ItemsSource = _view;

            DataContext = this;
            FilteredCount = Assets.Count;
        }

        private bool FilterPredicate(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            if (obj is not BindAssetItem item) return false;
            return item.Name.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyFilter()
        {
            _view?.Refresh();
            FilteredCount = _view?.Cast<BindAssetItem>().Count() ?? 0;
        }

        private void OnItemClicked(object sender, MouseButtonEventArgs e)
        {
            // 点击整行切换勾选（CheckBox 自身点击已处理，这里处理点击行其它区域）
            if (sender is FrameworkElement fe && fe.DataContext is BindAssetItem item)
            {
                // 若点击源头在 CheckBox 内部则跳过（避免重复切换）
                var src = e.OriginalSource as DependencyObject;
                while (src is not null)
                {
                    if (src is CheckBox) return;
                    src = System.Windows.Media.VisualTreeHelper.GetParent(src);
                }
                item.IsChecked = !item.IsChecked;
            }
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            SelectedAssets = Assets.Where(x => x.IsChecked).Select(x => x.Asset).ToList();
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
