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

        /// <summary>抽屉内试听音频</summary>
        private void OnDrawerPlayAudioClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.AssetsViewModel vm || vm.SelectedCard is not { } card) return;
            if (string.IsNullOrEmpty(card.AudioPath) || !File.Exists(card.AudioPath)) return;

            var path = card.AudioPath;
            if (_player.Source is not null && _player.Source.OriginalString == path)
            {
                if (_player.Position > TimeSpan.Zero && _player.Position < _player.NaturalDuration)
                    _player.Pause();
                else
                    _player.Play();
                return;
            }

            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
        }

        // ==================== 表格行双击 ====================

        /// <summary>双击表格行打开资产详情抽屉</summary>
        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid || grid.SelectedItem is not ViewModels.AssetCardItem card) return;
            if (DataContext is ViewModels.AssetsViewModel vm)
                vm.OpenDrawerCommand.Execute(card);
        }

        // ==================== 音频播放 ====================

        /// <summary>点击播放/暂停音频（表格行上的 🎵 按钮）</summary>
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

        // ==================== 图片放大 ====================

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
            DetailSubtitle.Text = card.TypeDisplay;
            DetailContent.Children.Clear();

            // ===== 媒体预览卡片 =====
            if (card.HasImage && !string.IsNullOrEmpty(card.ImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(card.ImagePath, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();

                    var img = new Image
                    {
                        Source = bmp,
                        MaxHeight = 220,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 0, 12)
                    };
                    DetailContent.Children.Add(MediaCard(img));
                }
                catch { /* 图片加载失败则跳过 */ }
            }
            else if (card.HasAudio && !string.IsNullOrEmpty(card.AudioPath))
            {
                var audioPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                audioPanel.Children.Add(new TextBlock
                {
                    Text = "🎵",
                    FontSize = 36,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 12)
                });
                var playBtn = new Button
                {
                    Content = "▶ 试听",
                    Style = (Style)FindResource("PrimaryButton"),
                    FontSize = 13,
                    Padding = new Thickness(20, 6, 20, 6),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Tag = card.AudioPath,
                    Cursor = Cursors.Hand
                };
                playBtn.Click += OnDetailPlayClick;
                audioPanel.Children.Add(playBtn);
                DetailContent.Children.Add(MediaCard(audioPanel));
            }

            // ===== 元信息（引用项目） =====
            DetailContent.Children.Add(MetaRow("🔗 引用项目", card.RefProjects));

            // ===== 分区卡片 =====
            var hasSection = false;
            if (!string.IsNullOrWhiteSpace(card.Description))
            {
                DetailContent.Children.Add(SectionCard("📝 描述", card.Description));
                hasSection = true;
            }
            if (!string.IsNullOrWhiteSpace(card.Prompt))
            {
                DetailContent.Children.Add(SectionCard("✨ 提示词（正向）", card.Prompt));
                hasSection = true;
            }
            if (!string.IsNullOrWhiteSpace(card.NegativePrompt))
            {
                DetailContent.Children.Add(SectionCard("🚫 提示词（反向）", card.NegativePrompt));
                hasSection = true;
            }

            // ===== 子资产 =====
            if (card.HasChildren)
            {
                var childPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                childPanel.Children.Add(SectionHeader("🧩 子资产"));
                var wrap = new WrapPanel();
                foreach (var c in card.Children)
                {
                    var chip = new Border
                    {
                        Background = (Brush)FindResource("PrimaryLightBrush"),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(10, 4, 10, 4),
                        Margin = new Thickness(0, 0, 8, 8)
                    };
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(new TextBlock
                    {
                        Text = c.TypeDisplay,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("PrimaryBrush"),
                        Margin = new Thickness(0, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    sp.Children.Add(new TextBlock
                    {
                        Text = c.Name,
                        FontSize = 12,
                        Foreground = (Brush)FindResource("TextSecondaryBrush"),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    chip.Child = sp;
                    wrap.Children.Add(chip);
                }
                childPanel.Children.Add(wrap);
                DetailContent.Children.Add(childPanel);
                hasSection = true;
            }

            // 无内容占位
            if (!hasSection)
            {
                DetailContent.Children.Add(new TextBlock
                {
                    Text = "该资产暂无描述与提示词",
                    FontSize = 13,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 24, 0, 0)
                });
            }

            DrawerAnimationHelper.Show(DetailDrawerRoot, DetailDrawerMask, DetailDrawerPanel, DrawerDirection.Right);
        }

        /// <summary>详情抽屉内试听音频</summary>
        private void OnDetailPlayClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string path || !File.Exists(path)) return;

            if (_player.Source is not null && _player.Source.OriginalString == path)
            {
                if (_player.Position > TimeSpan.Zero && _player.Position < _player.NaturalDuration)
                {
                    _player.Pause();
                    btn.Content = "▶ 试听";
                }
                else
                {
                    _player.Play();
                    btn.Content = "⏸ 暂停";
                }
                return;
            }

            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
            btn.Content = "⏸ 暂停";
        }

        /// <summary>媒体预览卡片（图片/音频容器）</summary>
        private static Border MediaCard(UIElement content)
        {
            var border = new Border
            {
                Background = (Brush)System.Windows.Application.Current.FindResource("PageBgBrush"),
                BorderBrush = (Brush)System.Windows.Application.Current.FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 16)
            };
            border.Child = content;
            return border;
        }

        /// <summary>元信息行（图标 + 标签 + 值）</summary>
        private static Border MetaRow(string label, string value)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)System.Windows.Application.Current.FindResource("TextMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 12,
                Foreground = (Brush)System.Windows.Application.Current.FindResource("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            return new Border { Child = sp };
        }

        /// <summary>分区标题（图标 + 文字）</summary>
        private static StackPanel SectionHeader(string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)System.Windows.Application.Current.FindResource("TextPrimaryBrush")
            });
            return sp;
        }

        /// <summary>分区卡片（浅底圆角卡片 + 标题 + 正文）</summary>
        private static Border SectionCard(string title, string body)
        {
            var panel = new StackPanel();
            panel.Children.Add(SectionHeader(title));
            panel.Children.Add(new TextBlock
            {
                Text = body,
                FontSize = 13,
                Foreground = (Brush)System.Windows.Application.Current.FindResource("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            return new Border
            {
                Background = (Brush)System.Windows.Application.Current.FindResource("PageBgBrush"),
                BorderBrush = (Brush)System.Windows.Application.Current.FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = panel
            };
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
