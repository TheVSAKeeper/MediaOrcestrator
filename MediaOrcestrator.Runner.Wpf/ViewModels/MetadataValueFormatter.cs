using MediaOrcestrator.Modules;
using System.ComponentModel;
using System.Globalization;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

internal static class MetadataValueFormatter
{
    public static string Format(MetadataItem meta) => Format(meta.Value, meta.DisplayType);

    public static string Format(string? value, string? displayType)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(displayType))
        {
            return value ?? string.Empty;
        }

        if (displayType == "ByteSize" && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
        {
            return FormatFileSize(bytes);
        }

        if (displayType == "Bitrate" && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitsPerSecond))
        {
            return FormatBitrate(bitsPerSecond);
        }

        var targetType = Type.GetType(displayType);

        if (targetType is null)
        {
            return value;
        }

        try
        {
            var converted = TypeDescriptor.GetConverter(targetType).ConvertFromInvariantString(value);

            return converted switch
            {
                DateTime dt => dt.ToString("g", CultureInfo.CurrentCulture),
                int n => n.ToString("N0", CultureInfo.CurrentCulture),
                long l => l.ToString("N0", CultureInfo.CurrentCulture),
                double d => d.ToString("0.##", CultureInfo.CurrentCulture),
                _ => converted?.ToString() ?? value,
            };
        }
        catch
        {
            return value;
        }
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} Б";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} КБ";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):F1} МБ";
        }

        return $"{bytes / (1024.0 * 1024 * 1024):F2} ГБ";
    }

    public static string FormatBitrate(long bitsPerSecond)
    {
        if (bitsPerSecond < 1000)
        {
            return $"{bitsPerSecond} бит/с";
        }

        if (bitsPerSecond < 1_000_000)
        {
            return $"{bitsPerSecond / 1000.0:F0} Кбит/с";
        }

        return $"{bitsPerSecond / 1_000_000.0:F2} Мбит/с";
    }
}
