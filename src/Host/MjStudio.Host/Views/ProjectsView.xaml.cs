using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MjStudio.Host.Services;

namespace MjStudio.Host.Views
{
    public partial class ProjectsView : UserControl
    {
        public ProjectsView()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (DataContext is ViewModels.ProjectsViewModel vm)
                    vm.View = this;
            };
        }

        // ==================== 抽屉 ====================

        /// <summary>打开抽屉：遮罩淡入 + 面板从右滑入</summary>
        public void ShowDrawer()
        {
            DrawerAnimationHelper.Show(DrawerRoot, DrawerMask, DrawerPanel, DrawerDirection.Right);
        }

        /// <summary>关闭抽屉：遮罩淡出 + 面板向右滑出</summary>
        public void HideDrawer()
        {
            DrawerAnimationHelper.Hide(DrawerRoot, DrawerMask, DrawerPanel, DrawerDirection.Right);
        }

        private void OnDrawerMaskClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.ProjectsViewModel vm)
                vm.CloseDrawerCommand.Execute(null);
        }

        // ==================== 卡片点击 ====================

        /// <summary>点击项目卡片（非按钮区域）打开详情抽屉</summary>
        private void OnCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Border border || border.DataContext is not Domain.Models.Project project) return;
            if (DataContext is ViewModels.ProjectsViewModel vm)
                vm.OpenDrawerCommand.Execute(project);
        }

        // ==================== 新建项目弹窗 ====================

        /// <summary>打开弹窗：遮罩淡入 + 面板缩放弹出</summary>
        public void ShowCreateDialog()
        {
            CreateDialogRoot.Visibility = Visibility.Visible;
            AnimateMaskIn(CreateDialogMask);
            ScaleIn(CreateDialogPanel);
            FocusNewNameBox();
        }

        /// <summary>关闭弹窗</summary>
        public void HideCreateDialog()
        {
            AnimateMaskOut(CreateDialogMask, () =>
            {
                CreateDialogRoot.Visibility = Visibility.Collapsed;
                CreateDialogPanel.RenderTransform = null;
            });
            ScaleOut(CreateDialogPanel);
        }

        private void OnDialogMaskClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.ProjectsViewModel vm)
                vm.CancelCreateCommand.Execute(null);
        }

        /// <summary>聚焦项目名输入框</summary>
        public void FocusNewNameBox()
        {
            NewNameBox.Focus();
            NewNameBox.SelectAll();
        }

        // ==================== 弹窗动画工具 ====================

        private static void AnimateMaskIn(Border mask)
        {
            mask.Opacity = 0;
            mask.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        }

        private static void AnimateMaskOut(Border mask, Action onDone)
        {
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            anim.Completed += (_, _) => onDone();
            mask.BeginAnimation(OpacityProperty, anim);
        }

        private static void ScaleIn(FrameworkElement panel)
        {
            var transform = new ScaleTransform(0.92, 0.92)
            {
                CenterX = panel.ActualWidth / 2,
                CenterY = panel.ActualHeight / 2
            };
            panel.RenderTransform = transform;
            panel.Opacity = 0;
            transform.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            transform.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            panel.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
        }

        private static void ScaleOut(FrameworkElement panel)
        {
            var transform = panel.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
            panel.RenderTransform = transform;
            transform.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(160)));
            transform.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(160)));
            panel.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
        }
    }
}
