using System.Globalization;
using System.Windows.Data;
using MjStudio.Domain.Shared;
using MjStudio.Host.ViewModels;

namespace MjStudio.Host.Converters
{
    /// <summary>环节枚举 → 显示名</summary>
    public class StageNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is StageEnum s ? ReviewViewModel.StageName(s) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>评审结论枚举 → 显示名</summary>
    public class DecisionNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is ReviewDecisionEnum d ? ReviewViewModel.DecisionName(d) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
