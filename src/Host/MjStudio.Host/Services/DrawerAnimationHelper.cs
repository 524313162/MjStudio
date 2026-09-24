using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MjStudio.Host.Services
{
    /// <summary>抽屉方向</summary>
    public enum DrawerDirection
    {
        Left,
        Right,
        Top,
        Bottom
    }

    /// <summary>
    /// 抽屉动画工具：支持上/下/左/右四个方向。
    /// 用法：Show(root, mask, panel, direction) / Hide(root, mask, panel, direction)。
    /// 面板从所属方向的外侧滑入，向同一方向滑出；遮罩淡入淡出。
    /// </summary>
    public static class DrawerAnimationHelper
    {
        private const int InDurationMs = 260;
        private const int OutDurationMs = 200;
        private const int MaskInMs = 200;
        private const int MaskOutMs = 180;

        /// <summary>打开抽屉：遮罩淡入 + 面板从指定方向滑入</summary>
        public static void Show(FrameworkElement root, Border mask, FrameworkElement panel, DrawerDirection direction)
        {
            root.Visibility = Visibility.Visible;
            AnimateMaskIn(mask);
            SlideIn(panel, direction);
        }

        /// <summary>关闭抽屉：遮罩淡出 + 面板向指定方向滑出</summary>
        public static void Hide(FrameworkElement root, Border mask, FrameworkElement panel, DrawerDirection direction)
        {
            AnimateMaskOut(mask, () =>
            {
                root.Visibility = Visibility.Collapsed;
                panel.RenderTransform = null;
            });
            SlideOut(panel, direction);
        }

        /// <summary>计算面板滑出屏幕所需的偏移量（优先用显式尺寸，回退实际尺寸）</summary>
        private static (double X, double Y) Offset(FrameworkElement panel, DrawerDirection direction)
        {
            var w = panel.Width > 0 ? panel.Width : panel.ActualWidth;
            var h = panel.Height > 0 ? panel.Height : panel.ActualHeight;
            return direction switch
            {
                DrawerDirection.Left => (-w, 0),
                DrawerDirection.Right => (w, 0),
                DrawerDirection.Top => (0, -h),
                DrawerDirection.Bottom => (0, h),
                _ => (0, 0)
            };
        }

        private static void SlideIn(FrameworkElement panel, DrawerDirection direction)
        {
            var (x, y) = Offset(panel, direction);
            var transform = new TranslateTransform(x, y);
            panel.RenderTransform = transform;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            if (x != 0)
                transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(InDurationMs)) { EasingFunction = ease });
            if (y != 0)
                transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(InDurationMs)) { EasingFunction = ease });
        }

        private static void SlideOut(FrameworkElement panel, DrawerDirection direction)
        {
            var (x, y) = Offset(panel, direction);
            var transform = panel.RenderTransform as TranslateTransform ?? new TranslateTransform();
            panel.RenderTransform = transform;
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            if (x != 0)
                transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(OutDurationMs)) { EasingFunction = ease });
            if (y != 0)
                transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(OutDurationMs)) { EasingFunction = ease });
        }

        private static void AnimateMaskIn(Border mask)
        {
            mask.Opacity = 0;
            mask.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(MaskInMs)));
        }

        private static void AnimateMaskOut(Border mask, Action onDone)
        {
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(MaskOutMs));
            anim.Completed += (_, _) => onDone();
            mask.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }
}
