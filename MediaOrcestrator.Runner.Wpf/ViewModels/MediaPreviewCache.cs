using MediaOrcestrator.Runner.Wpf.Converters;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

internal static class MediaPreviewCache
{
    private static readonly ILogger Logger = Log.ForContext(typeof(MediaPreviewCache));

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

        foreach (var candidate in Candidates(url))
        {
            try
            {
                var bytes = Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.IsFile
                    ? await File.ReadAllBytesAsync(uri.LocalPath).ConfigureAwait(false)
                    : await Http.GetByteArrayAsync(candidate).ConfigureAwait(false);

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(bytes);
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
        Cache.TryRemove(url, out _);
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
