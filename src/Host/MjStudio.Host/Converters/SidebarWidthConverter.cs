using System;
using System.Globalization;
using System.Windows.Data;

namespace MjStudio.Host.Converters
{
    /// <summary>
    /// 侧边栏宽度：展开 120；收起 56（保留图标栏）。参数：0=SidebarCollapsed。
    /// </summary>
    public class SidebarWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var collapsed = values is not null && values.Length > 0 && values[0] is true;
            return collapsed ? 56d : 120d;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
