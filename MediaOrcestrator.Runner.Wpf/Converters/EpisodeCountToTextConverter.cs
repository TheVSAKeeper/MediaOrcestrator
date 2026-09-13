using System.Globalization;
using System.Windows.Data;

namespace MediaOrcestrator.Runner.Wpf.Converters;

public sealed class EpisodeCountToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int count)
        {
            return string.Empty;
        }

        var mod100 = count % 100;
        var mod10 = count % 10;

        var word = mod100 is >= 11 and <= 14 ? "эпизодов"
            : mod10 == 1 ? "эпизод"
            : mod10 is >= 2 and <= 4 ? "эпизода"
            : "эпизодов";

        return $"{count} {word}";
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
