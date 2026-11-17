using MediaOrcestrator.Runner.Wpf.Converters;
using Serilog;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

internal static class MediaPreviewCache
{
    public const int DefaultDecodePixelWidth = 640;
    public const int DefaultCapacity = 48;

    private static readonly ILogger Logger = Log.ForContext(typeof(MediaPreviewCache));

    private static readonly AsyncLruCache<(string Url, int Width), BitmapImage> Cache = new(DefaultCapacity);

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public static Task<BitmapImage?> GetAsync(string url, int decodePixelWidth = DefaultDecodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult<BitmapImage?>(null);
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(decodePixelWidth, 0);

        return Cache.GetOrAddAsync((url, decodePixelWidth), static key => LoadAsync(key.Url, key.Width));
    }

    private static async Task<BitmapImage?> LoadAsync(string url, int decodePixelWidth)
    {
        var sw = Stopwatch.StartNew();

        foreach (var candidate in Candidates(url))
        {
            try
            {
                var bytes = Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.IsFile
                    ? await File.ReadAllBytesAsync(uri.LocalPath).ConfigureAwait(false)
                    : await Http.GetByteArrayAsync(candidate).ConfigureAwait(false);

                using var stream = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = decodePixelWidth;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();

                sw.Stop();
                Logger.Debug("Загружено превью {Url}: {Bytes} Б, {Elapsed} мс", candidate, bytes.Length, sw.ElapsedMilliseconds);

                return image;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Кандидат превью не загрузился: {Url}", candidate);
            }
        }

        sw.Stop();
        Logger.Warning("Не удалось загрузить превью {Url} (все кандидаты) за {Elapsed} мс", url, sw.ElapsedMilliseconds);

        return null;
    }

    private static IEnumerable<string> Candidates(string url)
    {
        if (StringToImageSourceConverter.IsYouTubeThumbnail(url))
        {
            var maxres = StringToImageSourceConverter.WithYouTubeThumbnail(url, "maxresdefault");

            if (!string.Equals(maxres, url, StringComparison.OrdinalIgnoreCase))
            {
                yield return maxres;
            }
        }

        yield return url;
    }
}
