using System;
using System.Collections.ObjectModel;
using System.Windows;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace MjStudio.Host.ViewModels
{
    /// <summary>
    /// 剧本页 ViewModel：按集展示剧本（标题/正文/时长），支持新建与编辑保存。
    /// 资产绑定统一在「分镜」环节按镜头进行，剧本页不再绑定资产。
    /// </summary>
    public class StoryViewModel : ViewModelBase
    {
        private readonly IServiceProvider _services;
        private readonly IStoryService _stories;
        private readonly IResourceService _resources;
        private readonly CurrentProject _current;

        public ObservableCollection<Story> Stories { get; } = new();

        /// <summary>全部剧本（未过滤）</summary>
        private readonly List<Story> _allStories = new();

        private Story? _selected;
        public Story? Selected
        {
            get => _selected;
            set => SetProperty(ref _selected, value);
        }

        // 搜索
        private string _searchTitle = "";
        public string SearchTitle { get => _searchTitle; set => SetProperty(ref _searchTitle, value); }

        public RelayCommand RefreshCommand { get; }
        public RelayCommand SearchCommand { get; }
        public RelayCommand CreateCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand DeleteCommand { get; }

        public StoryViewModel(IServiceProvider services)
        {
            _services = services;
            _stories = services.GetRequiredService<IStoryService>();
            _resources = services.GetRequiredService<IResourceService>();
            _current = services.GetRequiredService<CurrentProject>();

            RefreshCommand = new RelayCommand(Refresh);
            SearchCommand = new RelayCommand(Search);
            CreateCommand = new RelayCommand(Create);
            SaveCommand = new RelayCommand(Save);
            DeleteCommand = new RelayCommand(Delete);
            Refresh();
        }

        public void Refresh()
        {
            _allStories.Clear();
            if (_current.IsLoaded)
            {
                try
                {
                    var pid = _resources.GetProjectIdAsync().GetAwaiter().GetResult();
                    var list = _stories.GetByProjectAsync(pid).GetAwaiter().GetResult();
                    _allStories.AddRange(list.OrderBy(x => x.EpisodeNo));
                }
                catch { /* 未加载项目时忽略 */ }
            }
            ApplyFilter();
        }

        /// <summary>按标题关键字过滤列表（回车触发）</summary>
        private void Search()
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var kw = SearchTitle?.Trim() ?? "";
            var filtered = string.IsNullOrEmpty(kw)
                ? _allStories
                : _allStories.Where(s => s.Title?.Contains(kw, StringComparison.OrdinalIgnoreCase) == true).ToList();

            var prevId = Selected?.Id;
            Stories.Clear();
            foreach (var s in filtered) Stories.Add(s);

            // 默认选中第一集（或保持之前选中项）
            Selected = Stories.FirstOrDefault(s => s.Id == prevId) ?? Stories.FirstOrDefault();
        }

        private void Create()
        {
            if (!_current.IsLoaded)
            {
                MessageBox.Show("请先加载项目", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                var pid = _resources.GetProjectIdAsync().GetAwaiter().GetResult();
                // 集数自动递增（当前最大集数 + 1）
                var nextNo = _allStories.Count > 0 ? _allStories.Max(s => s.EpisodeNo) + 1 : 1;
                var story = new Story
                {
                    ProjectId = pid,
                    EpisodeNo = nextNo,
                    Title = $"第{nextNo}集",
                    Content = ""
                };
                _stories.CreateAsync(story).GetAwaiter().GetResult();
                Refresh();
                // 选中新建的剧本
                Selected = Stories.FirstOrDefault(s => s.Id == story.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show("创建剧本失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Save()
        {
            if (Selected is null) return;
            try
            {
                _stories.UpdateAsync(Selected).GetAwaiter().GetResult();
                MessageBox.Show("剧本已保存", "MjStudio", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Delete()
        {
            if (Selected is null) return;
            if (MessageBox.Show($"确定删除第 {Selected.EpisodeNo} 集剧本？", "MjStudio", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try
            {
                _stories.DeleteAsync(Selected.Id).GetAwaiter().GetResult();
                Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除失败：" + ex.Message, "MjStudio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
