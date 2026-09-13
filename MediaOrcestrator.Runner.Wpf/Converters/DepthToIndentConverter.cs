using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MediaOrcestrator.Runner.Wpf.Converters;

public sealed class DepthToIndentConverter : IValueConverter
{
    private const double StepWidth = 18;

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int depth)
        {
            return new Thickness(0);
        }

        return new Thickness(Math.Max(depth, 0) * StepWidth, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
