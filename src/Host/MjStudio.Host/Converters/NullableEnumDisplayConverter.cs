using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace MjStudio.Host.Converters
{
    /// <summary>
    /// 可空枚举 → Display(Name) 转换器；null 显示「全部」。
    /// 用法: Text="{Binding FilterType, Converter={StaticResource NullableEnumDisplay}}"
    /// </summary>
    public class NullableEnumDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is null) return "全部";
            if (value is Enum e)
            {
                var field = e.GetType().GetField(e.ToString());
                var attr = field?.GetCustomAttribute<DisplayAttribute>();
                return attr?.Name ?? e.ToString();
            }
            return value.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
