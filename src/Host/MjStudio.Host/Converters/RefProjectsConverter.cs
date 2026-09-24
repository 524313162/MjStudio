using System.Globalization;
using System.Windows.Data;
using MjStudio.Domain.Models;

namespace MjStudio.Host.Converters
{
    /// <summary>
    /// 资产 → 引用项目名文本（"项目A、项目B"；无则"未引用"）。
    /// 用法: Text="{Binding Converter={StaticResource RefProjects}}"
    /// </summary>
    public class RefProjectsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Asset a && a.ProjectRefs is { Count: > 0 })
            {
                var names = a.ProjectRefs
                    .Where(r => r.Project is not null)
                    .Select(r => r.Project!.Name)
                    .OrderBy(n => n)
                    .ToList();
                return names.Count == 0 ? "未引用" : string.Join("、", names);
            }
            return "未引用";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
