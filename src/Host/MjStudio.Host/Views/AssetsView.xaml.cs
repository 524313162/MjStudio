using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MjStudio.Host.Services;

namespace MjStudio.Host.Views
{
    public partial class AssetsView : UserControl
    {
        private readonly MediaPlayer _player = new();

        public AssetsView()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (DataContext is ViewModels.AssetsViewModel vm)
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
            if (DataContext is ViewModels.AssetsViewModel vm)
                vm.CloseDrawerCommand.Execute(null);
        }

        // ==================== 表格行双击 ====================

        /// <summary>双击表格行打开资产详情抽屉</summary>
        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid || grid.SelectedItem is not ViewModels.AssetCardItem card) return;
            if (DataContext is ViewModels.AssetsViewModel vm)
                vm.OpenDrawerCommand.Execute(card);
        }

        // ==================== 音频播放（问题6） ====================

        /// <summary>点击播放/暂停音频（卡片上的 ▶ 按钮）</summary>
        private void OnPlayAudioClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string path) return;
            e.Handled = true; // 阻止冒泡到卡片打开抽屉

            if (!File.Exists(path)) return;

            if (_player.Source is not null && _player.Source.OriginalString == path)
            {
                // 同一文件：切换播放/暂停
                if (_player.Position > TimeSpan.Zero && _player.Position < _player.NaturalDuration)
                    _player.Pause();
                else
                    _player.Play();
                return;
            }

            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
        }

        // ==================== 图片放大（问题6） ====================

        /// <summary>点击图片放大查看（弹出窗口）</summary>
        private void OnImageClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Image img || img.Source is not BitmapSource bmp) return;
            e.Handled = true; // 阻止冒泡到卡片打开抽屉

            var win = new Window
            {
                Title = "资产图片预览",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                MaxWidth = SystemParameters.WorkArea.Width * 0.9,
                MaxHeight = SystemParameters.WorkArea.Height * 0.9
            };
            var image = new Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                MaxWidth = SystemParameters.WorkArea.Width * 0.9,
                MaxHeight = SystemParameters.WorkArea.Height * 0.9,
                Margin = new Thickness(24),
                Cursor = Cursors.Hand
            };
            image.MouseLeftButtonDown += (_, _) => win.Close();
            win.Content = image;
            win.ShowDialog();
        }

        // ==================== 描述列点击：只读详情抽屉 ====================

        /// <summary>点击描述列：打开只读详情抽屉，完整展示描述 + 全部提示词</summary>
        private void OnDescriptionClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not ViewModels.AssetCardItem card) return;
            e.Handled = true; // 阻止冒泡到行双击

            DetailTitle.Text = card.Name;
            DetailSubtitle.Text = $"类型：{card.TypeDisplay}";
            DetailContent.Children.Clear();

            // 描述
            if (!string.IsNullOrWhiteSpace(card.Description))
            {
                DetailContent.Children.Add(SectionTitle("📝 描述"));
                DetailContent.Children.Add(SectionBody(card.Description));
            }

            // 正向提示词
            if (!string.IsNullOrWhiteSpace(card.Prompt))
            {
                DetailContent.Children.Add(SectionTitle("✨ 提示词（正向）"));
                DetailContent.Children.Add(SectionBody(card.Prompt));
            }

            // 反向提示词
            if (!string.IsNullOrWhiteSpace(card.NegativePrompt))
            {
                DetailContent.Children.Add(SectionTitle("🚫 提示词（反向）"));
                DetailContent.Children.Add(SectionBody(card.NegativePrompt));
            }

            // 无内容占位
            if (DetailContent.Children.Count == 0)
            {
                DetailContent.Children.Add(new TextBlock
                {
                    Text = "该资产暂无描述与提示词",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6))
                });
            }

            DrawerAnimationHelper.Show(DetailDrawerRoot, DetailDrawerMask, DetailDrawerPanel, DrawerDirection.Right);
        }

        /// <summary>关闭只读详情抽屉</summary>
        private void OnDetailDrawerClose(object sender, RoutedEventArgs e)
        {
            DrawerAnimationHelper.Hide(DetailDrawerRoot, DetailDrawerMask, DetailDrawerPanel, DrawerDirection.Right);
        }

        /// <summary>点击详情抽屉遮罩关闭</summary>
        private void OnDetailDrawerMaskClick(object sender, MouseButtonEventArgs e)
        {
            DrawerAnimationHelper.Hide(DetailDrawerRoot, DetailDrawerMask, DetailDrawerPanel, DrawerDirection.Right);
        }

        /// <summary>详情抽屉小节标题</summary>
        private static TextBlock SectionTitle(string text) => new()
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0x6E, 0xF5)),
            Margin = new Thickness(0, 12, 0, 6)
        };

        /// <summary>详情抽屉小节正文（可选中复制）</summary>
        private static TextBox SectionBody(string text) => new()
        {
            Text = text,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x3A, 0x45)),
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        // ==================== 悬停预览（表格名称列） ====================

        /// <summary>鼠标移入名称列：显示图片预览或音频播放面板</summary>
        private void OnNameMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not ViewModels.AssetCardItem card) return;

            // 图片资产：显示大图
            if (card.HasImage && !string.IsNullOrEmpty(card.ImagePath) && File.Exists(card.ImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(card.ImagePath, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    HoverImage.Source = bmp;
                    HoverImage.Visibility = Visibility.Visible;
                    HoverAudioPanel.Visibility = Visibility.Collapsed;
                    HoverNoMedia.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    ShowHoverNoMedia();
                }
            }
            // 音频资产：显示播放面板
            else if (card.HasAudio && !string.IsNullOrEmpty(card.AudioPath) && File.Exists(card.AudioPath))
            {
                HoverImage.Visibility = Visibility.Collapsed;
                HoverAudioPanel.Visibility = Visibility.Visible;
                HoverNoMedia.Visibility = Visibility.Collapsed;
                HoverAudioName.Text = card.Name;
                HoverPlayButton.Tag = card.AudioPath;
                HoverPlayButton.Content = "▶";
            }
            else
            {
                ShowHoverNoMedia();
            }

            HoverPopup.IsOpen = true;
        }

        /// <summary>鼠标移出名称列：关闭预览</summary>
        private void OnNameMouseLeave(object sender, MouseEventArgs e)
        {
            HoverPopup.IsOpen = false;
            HoverImage.Source = null;
        }

        /// <summary>悬停预览中的音频播放/暂停</summary>
        private void OnHoverPlayClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string path) return;
            if (!File.Exists(path)) return;

            if (_player.Source is not null && _player.Source.OriginalString == path)
            {
                if (_player.Position > TimeSpan.Zero && _player.Position < _player.NaturalDuration)
                {
                    _player.Pause();
                    btn.Content = "▶";
                }
                else
                {
                    _player.Play();
                    btn.Content = "⏸";
                }
                return;
            }

            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
            btn.Content = "⏸";
        }

        /// <summary>无媒体时的占位提示</summary>
        private void ShowHoverNoMedia()
        {
            HoverImage.Visibility = Visibility.Collapsed;
            HoverAudioPanel.Visibility = Visibility.Collapsed;
            HoverNoMedia.Visibility = Visibility.Visible;
        }
    }
}
