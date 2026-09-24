using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows;
using MjStudio.Application.Shared.Services;
using MjStudio.Domain.Models;
using MjStudio.Domain.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace MjStudio.Host.ViewModels
{
    /// <summary>
    /// 评审页 ViewModel：展示各环节评审结论（评审由 agent 经 API 写入，此处只读查看）。
    /// </summary>
    public class ReviewViewModel : ViewModelBase
    {
        private readonly IServiceProvider _services;
        private readonly IReviewService _reviews;
        private readonly IResourceService _resources;
        private readonly CurrentProject _current;

        public ObservableCollection<Review> Reviews { get; } = new();

        private Review? _selected;
        public Review? Selected { get => _selected; set => SetProperty(ref _selected, value); }

        public StageEnum[] Stages { get; } = Enum.GetValues<StageEnum>();

        public RelayCommand RefreshCommand { get; }

        public ReviewViewModel(IServiceProvider services)
        {
            _services = services;
            _reviews = services.GetRequiredService<IReviewService>();
            _resources = services.GetRequiredService<IResourceService>();
            _current = services.GetRequiredService<CurrentProject>();

            RefreshCommand = new RelayCommand(Refresh);
            Refresh();
        }

        /// <summary>取环节显示名（Display 特性）</summary>
        public static string StageName(StageEnum stage)
        {
            var type = typeof(StageEnum);
            var field = type.GetField(stage.ToString());
            var attr = field?.GetCustomAttribute<DisplayAttribute>();
            return attr?.Name ?? stage.ToString();
        }

        /// <summary>取结论显示名</summary>
        public static string DecisionName(ReviewDecisionEnum d) => d switch
        {
            ReviewDecisionEnum.Pending => "待评审",
            ReviewDecisionEnum.Approved => "✅ 通过",
            ReviewDecisionEnum.Revision => "🔄 打回",
            ReviewDecisionEnum.Escalated => "⚠️ 已升级",
            _ => d.ToString()
        };

        public void Refresh()
        {
            Reviews.Clear();
            if (!_current.IsLoaded) return;
            try
            {
                var pid = _resources.GetProjectIdAsync().GetAwaiter().GetResult();
                var list = _reviews.GetAllAsync(pid).GetAwaiter().GetResult();
                foreach (var r in list.OrderBy(x => x.Stage)) Reviews.Add(r);
            }
            catch { /* 未加载项目时忽略 */ }
        }
    }
}
