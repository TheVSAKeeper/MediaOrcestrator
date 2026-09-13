using MahApps.Metro.IconPacks;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MediaOrcestrator.Runner.Wpf.Converters;

public sealed class OperationSourceChipStateToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OperationSourceChipState state)
        {
            return DependencyProperty.UnsetValue;
        }

        return state switch
        {
            OperationSourceChipState.Synced => PackIconLucideKind.Check,
            OperationSourceChipState.OutOfSync => PackIconLucideKind.Pencil,
            _ => PackIconLucideKind.Ban,
        };
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
