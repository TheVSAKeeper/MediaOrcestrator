using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Globalization;
using System.Windows.Data;

namespace MediaOrcestrator.Runner.Wpf.Converters;

public sealed class SeriesCoverageLookupConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length >= 2
            && values[0] is string seriesName
            && values[1] is IReadOnlyDictionary<string, IReadOnlyList<SeriesSourceCoverage>> map
            && map.TryGetValue(seriesName, out var coverage))
        {
            return coverage;
        }

        return Array.Empty<SeriesSourceCoverage>();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
