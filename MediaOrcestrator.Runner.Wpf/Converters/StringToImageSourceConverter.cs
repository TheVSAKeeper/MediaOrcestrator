using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MediaOrcestrator.Runner.Wpf.Converters;

internal sealed partial class StringToImageSourceConverter : IValueConverter
{
    [GeneratedRegex(@"(?<=/vi/[^/]+/)[^/]+(?=\.jpg)", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeThumbnailName();

    [GeneratedRegex(@"/(?:hq|sd)?default\.jpg", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeFourThreeThumbnail();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || !TryResolve(text, out var uri))
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.DelayCreation;
        image.UriSource = uri;
        image.EndInit();

        return image;
    }

    public static bool CanRender(string? value) => TryResolve(value, out _);

    public static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmed = value.Trim();

        if (trimmed.Contains("ytimg.com/vi_webp/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed
                .Replace("/vi_webp/", "/vi/", StringComparison.OrdinalIgnoreCase)
                .Replace(".webp", ".jpg", StringComparison.OrdinalIgnoreCase);
        }

        if (IsYouTubeThumbnail(trimmed))
        {
            trimmed = YouTubeFourThreeThumbnail().Replace(trimmed, "/mqdefault.jpg");
        }

        return trimmed;
    }

    public static bool IsYouTubeThumbnail(string? url) =>
        !string.IsNullOrEmpty(url)
        && (url.Contains("ytimg.com/vi/", StringComparison.OrdinalIgnoreCase)
            || url.Contains("youtube.com/vi/", StringComparison.OrdinalIgnoreCase));

    public static string WithYouTubeThumbnail(string url, string name) =>
        YouTubeThumbnailName().Replace(url, name);

    private static bool TryResolve(string? value, out Uri uri)
    {
        uri = null!;

        var normalized = NormalizeUrl(value);

        if (string.IsNullOrWhiteSpace(normalized)
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme is not "http" and not "https" and not "file")
        {
            return false;
        }

        if (parsed.IsFile && !File.Exists(parsed.LocalPath))
        {
            return false;
        }

        if (parsed.AbsolutePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = parsed;

        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
