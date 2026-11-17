using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class MediaPreviewCacheTests
{
    [Test]
    public async Task GetOrAddAsync_ПриПревышенииЕмкости_ВытесняетНаименееВостребованныйИОбновляетПозициюПриПовторномДоступе()
    {
        var cache = new AsyncLruCache<string, string>(2, StringComparer.Ordinal);
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);

        Task<string?> Factory(string key)
        {
            calls[key] = calls.GetValueOrDefault(key) + 1;
            return Task.FromResult<string?>($"{key}:{calls[key]}");
        }

        var firstA = await cache.GetOrAddAsync("a", Factory);
        var firstB = await cache.GetOrAddAsync("b", Factory);
        var hitA = await cache.GetOrAddAsync("a", Factory);
        var firstC = await cache.GetOrAddAsync("c", Factory);
        var stillCachedA = await cache.GetOrAddAsync("a", Factory);
        var reloadedB = await cache.GetOrAddAsync("b", Factory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstA, Is.EqualTo("a:1"));
            Assert.That(firstB, Is.EqualTo("b:1"));
            Assert.That(hitA, Is.EqualTo("a:1"));
            Assert.That(firstC, Is.EqualTo("c:1"));
            Assert.That(stillCachedA, Is.EqualTo("a:1"));
            Assert.That(reloadedB, Is.EqualTo("b:2"));
            Assert.That(calls["a"], Is.EqualTo(1));
            Assert.That(calls["b"], Is.EqualTo(2));
            Assert.That(calls["c"], Is.EqualTo(1));
            Assert.That(cache.Count, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetOrAddAsync_ПриПараллельныхЗапросахОдногоКлюча_ВызываетФабрикуОдинРаз()
    {
        var cache = new AsyncLruCache<string, string>(4, StringComparer.Ordinal);
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => cache.GetOrAddAsync("same-key", _ =>
            {
                Interlocked.Increment(ref calls);
                return gate.Task;
            }))
            .ToArray();

        gate.SetResult("loaded");
        var results = await Task.WhenAll(tasks);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(tasks.Distinct().Count(), Is.EqualTo(1));
            Assert.That(results, Has.All.EqualTo("loaded"));
        }
    }

    [Test]
    public async Task GetOrAddAsync_ПриВозвратеNullИлиИсключении_УдаляетЗаписьИПозволяетПовторнуюПопытку()
    {
        var cache = new AsyncLruCache<string, string>(4, StringComparer.Ordinal);
        var nullAttempt = 0;

        var firstNull = await cache.GetOrAddAsync("null-key", _ =>
        {
            nullAttempt++;
            return Task.FromResult<string?>(nullAttempt == 1 ? null : "recovered");
        });
        var countAfterNull = cache.Count;
        var afterNullRetry = await cache.GetOrAddAsync("null-key", _ =>
        {
            nullAttempt++;
            return Task.FromResult<string?>("recovered");
        });

        var errorCache = new AsyncLruCache<string, string>(4, StringComparer.Ordinal);
        var failAttempt = 0;
        InvalidOperationException? caught = null;

        try
        {
            await errorCache.GetOrAddAsync("err-key", _ =>
            {
                failAttempt++;
                return Task.FromException<string?>(new InvalidOperationException("boom"));
            });
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        var countAfterError = errorCache.Count;
        var afterErrorRetry = await errorCache.GetOrAddAsync("err-key", _ =>
        {
            failAttempt++;
            return Task.FromResult<string?>("ok");
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstNull, Is.Null);
            Assert.That(countAfterNull, Is.Zero);
            Assert.That(afterNullRetry, Is.EqualTo("recovered"));
            Assert.That(nullAttempt, Is.EqualTo(2));
            Assert.That(caught, Is.Not.Null);
            Assert.That(countAfterError, Is.Zero);
            Assert.That(afterErrorRetry, Is.EqualTo("ok"));
            Assert.That(failAttempt, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetOrAddAsync_ПриПозднемСбоеВытесненнойЗаписи_НеУдаляетНовуюЗапись()
    {
        var cache = new AsyncLruCache<string, string>(2, StringComparer.Ordinal);
        var staleGate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var staleTask = cache.GetOrAddAsync("x", _ => staleGate.Task);
        await cache.GetOrAddAsync("y", _ => Task.FromResult<string?>("y-ok"));
        await cache.GetOrAddAsync("z", _ => Task.FromResult<string?>("z-ok"));

        var freshCalls = 0;
        var freshTask = cache.GetOrAddAsync("x", _ =>
        {
            freshCalls++;
            return Task.FromResult<string?>("x-fresh");
        });

        staleGate.SetResult(null);

        var staleResult = await staleTask;
        var freshResult = await freshTask;
        var cachedResult = await cache.GetOrAddAsync("x", _ =>
        {
            freshCalls++;
            return Task.FromResult<string?>("unexpected");
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(staleResult, Is.Null);
            Assert.That(freshResult, Is.EqualTo("x-fresh"));
            Assert.That(cachedResult, Is.EqualTo("x-fresh"));
            Assert.That(freshCalls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetAsync_ЛокальныйPng3840x2160_ВозвращаетЗамороженныйКадрСШириной640()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"preview-{Guid.NewGuid():N}.png");

        try
        {
            const int width = 3840;
            const int height = 2160;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using (var stream = File.Create(tempFile))
            {
                encoder.Save(stream);
            }

            var uri = new Uri(tempFile).AbsoluteUri;
            var image = await MediaPreviewCache.GetAsync(uri);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(image, Is.Not.Null);
                Assert.That(image!.IsFrozen, Is.True);
                Assert.That(image.PixelWidth, Is.EqualTo(MediaPreviewCache.DefaultDecodePixelWidth));
                Assert.That(image.PixelHeight, Is.EqualTo(360));
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
