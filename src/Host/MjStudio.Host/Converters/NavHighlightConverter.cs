using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MjStudio.Host.Converters
{
    /// <summary>
    /// 导航高亮：当 CurrentPage == 按钮参数时返回高亮前景色，否则返回普通色。
    /// 用法: Foreground="{Binding CurrentPage, Converter={StaticResource NavFg}, ConverterParameter=projects}"
    /// </summary>
    public class NavHighlightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string current && parameter is string page && current == page)
                return new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            return new SolidColorBrush(Color.FromRgb(0xA0, 0xAE, 0xC0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 导航背景高亮：当 CurrentPage == 按钮参数时返回激活背景，否则透明。
    /// </summary>
    public class NavBgConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string current && parameter is string page && current == page)
                return new SolidColorBrush(Color.FromRgb(0x2D, 0x35, 0x48));
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
