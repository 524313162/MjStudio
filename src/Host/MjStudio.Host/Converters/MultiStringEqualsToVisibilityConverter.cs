using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MjStudio.Host.Converters
{
    /// <summary>
    /// 多值比较：当第一个值等于第二个值时返回 Visible，否则 Collapsed。
    /// 用于在列表中标记"当前"项目。
    /// </summary>
    public class MultiStringEqualsToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is string a && values[1] is string b)
                return a == b ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
