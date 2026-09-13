using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MediaOrcestrator.Runner.Wpf.Converters;

public sealed class BoolToVisibilityHiddenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Hidden;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}
