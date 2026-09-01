using System.Globalization;
using System.Windows.Data;

namespace Waa.App.ViewModels;

public sealed class MissingBolSummaryDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        const string prefix = "Missing BOL: ";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        var report = text[prefix.Length..]
            .Replace(" open", " matched", StringComparison.OrdinalIgnoreCase);
        return $"Missing BOL file: {report}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}