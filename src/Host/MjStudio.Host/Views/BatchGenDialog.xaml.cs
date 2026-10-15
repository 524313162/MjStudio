using System.Windows;
using System.Windows.Controls;
using MjStudio.Host.ViewModels;

namespace MjStudio.Host.Views
{
    /// <summary>
    /// 批量生成弹窗：3 个标签页（图片/声音/视频），卡片右上角按钮根据状态切换「去生成 / 删除」。
    /// </summary>
    public partial class BatchGenDialog : Window
    {
        private readonly BatchGenViewModel _vm;

        public BatchGenDialog(BatchGenViewModel vm)
        {
            _vm = vm;
            InitializeComponent();
            DataContext = vm;
        }

        /// <summary>
        /// 卡片右上角按钮：已有内容 → 清理；无内容 → 生成。
        /// </summary>
        private void OnCardActionClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is BatchGenCardItem card)
            {
                if (card.HasContent)
                    _vm.ClearSingle(card);
                else
                    _vm.GenerateSingle(card);
            }
        }

        /// <summary>
        /// 点击已生成图片：弹出放大预览窗口。
        /// </summary>
        private void OnImageClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Image img || img.Source is not System.Windows.Media.Imaging.BitmapSource bmp) return;
            e.Handled = true;

            var win = new Window
            {
                Title = "图片预览",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 0, 0)),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                MaxWidth = SystemParameters.WorkArea.Width * 0.9,
                MaxHeight = SystemParameters.WorkArea.Height * 0.9
            };
            var image = new Image
            {
                Source = bmp,
                Stretch = System.Windows.Media.Stretch.Uniform,
                MaxWidth = SystemParameters.WorkArea.Width * 0.9,
                MaxHeight = SystemParameters.WorkArea.Height * 0.9,
                Margin = new Thickness(24),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            image.MouseLeftButtonDown += (_, _) => win.Close();
            win.Content = image;
            win.ShowDialog();
        }
    }
}
