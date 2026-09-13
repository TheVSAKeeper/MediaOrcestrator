using MahApps.Metro.IconPacks;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MediaOrcestrator.Runner.Wpf.Converters;

public sealed class OperationRowStatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OperationRowStatus status)
        {
            return DependencyProperty.UnsetValue;
        }

        return status switch
        {
            OperationRowStatus.Applied => PackIconLucideKind.CircleCheck,
            OperationRowStatus.Failed => PackIconLucideKind.CircleX,
            OperationRowStatus.Skipped => PackIconLucideKind.CircleSlash,
            _ => PackIconLucideKind.Clock,
        };
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
