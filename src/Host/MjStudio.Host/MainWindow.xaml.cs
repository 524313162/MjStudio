using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MjStudio.Host.ViewModels;

namespace MjStudio.Host
{
    /// <summary>
    /// 主窗口（自定义无边框窗口：圆角 + 阴影 + 标题栏拖动 + 窗口控制按钮）
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            StateChanged += (_, _) => UpdateMaximizeState();
        }

        // ==================== 最大化限制到工作区（不遮任务栏） ====================

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                GetMonitorInfo(monitor, ref monitorInfo);
                var rcWork = monitorInfo.rcWork;
                var rcMonitor = monitorInfo.rcMonitor;
                mmi.ptMaxPosition.X = Math.Abs(rcWork.left - rcMonitor.left);
                mmi.ptMaxPosition.Y = Math.Abs(rcWork.top - rcMonitor.top);
                mmi.ptMaxSize.X = Math.Abs(rcWork.right - rcWork.left);
                mmi.ptMaxSize.Y = Math.Abs(rcWork.bottom - rcWork.top);
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        /// <summary>标题栏拖动移动窗口</summary>
        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            // 双击标题栏切换最大化
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            if (WindowState == WindowState.Normal)
                DragMove();
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
            => ToggleMaximize();

        private void OnCloseClick(object sender, RoutedEventArgs e)
            => Close();

        private void ToggleMaximize()
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        /// <summary>
        /// 最大化时去掉边距/圆角/阴影以铺满屏幕；还原时恢复。
        /// </summary>
        private void UpdateMaximizeState()
        {
            if (WindowState == WindowState.Maximized)
            {
                ShadowBorder.Margin = new Thickness(0);
                ShadowBorder.CornerRadius = new CornerRadius(0);
                ShadowBorder.Effect = null;
                RootBorder.Margin = new Thickness(0);
                RootBorder.CornerRadius = new CornerRadius(0);
                RootBorder.BorderThickness = new Thickness(0);
                MaximizeButton.Content = "❐";
            }
            else
            {
                ShadowBorder.Margin = new Thickness(16);
                ShadowBorder.CornerRadius = new CornerRadius(12);
                ShadowBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 0,
                    Opacity = 0.35,
                    Color = System.Windows.Media.Colors.Black
                };
                RootBorder.Margin = new Thickness(0);
                RootBorder.CornerRadius = new CornerRadius(12);
                RootBorder.BorderThickness = new Thickness(1);
                MaximizeButton.Content = "▢";
            }
        }
    }
}