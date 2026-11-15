using MediaOrcestrator.Runner.Wpf.Converters;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

internal static class MediaThumbnailCache
{
    private const int DecodePixelHeight = 80;

    private static readonly ILogger Logger = Log.ForContext(typeof(MediaThumbnailCache));

    private static readonly ConcurrentDictionary<string, Task<BitmapImage?>> Cache = new(StringComparer.Ordinal);

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public static Task<BitmapImage?> GetAsync(string? url)
    {
        var normalized = StringToImageSourceConverter.NormalizeUrl(url);

        if (string.IsNullOrWhiteSpace(normalized) || !StringToImageSourceConverter.CanRender(normalized))
        {
            return Task.FromResult<BitmapImage?>(null);
        }

        return Cache.GetOrAdd(normalized, LoadAsync);
    }

    private static async Task<BitmapImage?> LoadAsync(string url)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var bytes = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile
                ? await File.ReadAllBytesAsync(uri.LocalPath).ConfigureAwait(false)
                : await Http.GetByteArrayAsync(url).ConfigureAwait(false);

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelHeight = DecodePixelHeight;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();

            sw.Stop();
            Logger.Debug("Загружена обложка {Url}: {Bytes} Б, {Elapsed} мс", url, bytes.Length, sw.ElapsedMilliseconds);
            return image;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.Warning(ex, "Не удалось загрузить обложку {Url} за {Elapsed} мс", url, sw.ElapsedMilliseconds);
            return null;
        }
    }
}
