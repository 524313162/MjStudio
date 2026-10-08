using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace MjStudio.Host.Converters
{
    /// <summary>
    /// 多值文本 → 标签数组：按 / 、，, 等分隔符拆分并去空白。
    /// 用于项目卡片把「都市 / 现实 / 家庭」拆成独立标签，避免整块显示。
    /// 用法: ItemsSource="{Binding Genre, Converter={StaticResource TagSplit}, ConverterParameter='未分类'}"
    /// 当输入为空时，返回仅含 ConverterParameter（占位符）的单元素数组。
    /// </summary>
    public class TagSplitConverter : IValueConverter
    {
        private static readonly char[] Separators = { '/', '、', '，', ',', '|', '；', ';' };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value as string;
            if (string.IsNullOrWhiteSpace(s))
            {
                var placeholder = parameter as string;
                return new[] { string.IsNullOrWhiteSpace(placeholder) ? "未设置" : placeholder! };
            }

            var tags = s.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                       .Select(t => t.Trim())
                       .Where(t => t.Length > 0)
                       .ToList();

            return tags.Count > 0 ? tags : new[] { (parameter as string) ?? "未设置" };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
