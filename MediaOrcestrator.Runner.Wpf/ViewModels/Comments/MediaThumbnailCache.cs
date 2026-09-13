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

    public static Task<BitmapImage?> GetAsync(string url)
    {
        return Cache.GetOrAdd(url, LoadAsync);
    }

    private static async Task<BitmapImage?> LoadAsync(string url)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);

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
