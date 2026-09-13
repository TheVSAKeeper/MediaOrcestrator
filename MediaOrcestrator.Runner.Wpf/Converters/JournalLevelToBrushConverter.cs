using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.Converters;

public sealed class JournalLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OperationJournalLevel level)
        {
            return DependencyProperty.UnsetValue;
        }

        var key = level switch
        {
            OperationJournalLevel.Success => "State.Success",
            OperationJournalLevel.Warning => "State.Warning",
            OperationJournalLevel.Error => "State.Error",
            _ => "Fg.Muted",
        };

        var resource = Application.Current?.TryFindResource(key);

        if (resource is Brush brush)
        {
            return brush;
        }

        return level switch
        {
            OperationJournalLevel.Success => Brushes.DarkGreen,
            OperationJournalLevel.Warning => Brushes.DarkOrange,
            OperationJournalLevel.Error => Brushes.Firebrick,
            _ => Brushes.Gray,
        };
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
