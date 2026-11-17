using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
public sealed class GitHubReleaseProviderTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task GetLatestReleaseAsync_ПараллельныеЗапросыОдногоРепозитория_ВыполняетОдинHttpЗапрос()
    {
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 0;

        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            var index = Interlocked.Increment(ref requestCount);
            if (index == 1)
            {
                firstRequestStarted.TrySetResult();
            }

            await releaseGate.Task.WaitAsync(ct);
            return JsonResponse(HttpStatusCode.OK, CreateReleasePayload("v1.2.3", "app-v1.2.3.zip"), etag: "\"etag-1\"");
        });

        var provider = CreateProvider(handler, new FakeTimeProvider(BaseTime));

        var task1 = provider.GetLatestReleaseAsync("owner/repo", "app-v*.zip");
        await firstRequestStarted.Task;

        var task2 = provider.GetLatestReleaseAsync("owner/repo", "app-v*.zip");
        var task3 = provider.GetLatestReleaseAsync("owner/repo", "app-v*.zip");
        var task4 = provider.GetLatestReleaseAsync("owner/repo", "app-v*.zip");

        releaseGate.SetResult();
        var results = await Task.WhenAll(task1, task2, task3, task4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requestCount, Is.EqualTo(1));
            Assert.That(results, Has.All.Not.Null);
            Assert.That(results.Select(r => r!.TagName), Has.All.EqualTo("v1.2.3"));
        }
    }

    [Test]
    public async Task GetLatestReleaseAsync_ПовторныйЗапросВПределахTtl_ВозвращаетИзКэшаБезHttpЗапроса()
    {
        var requestCount = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            Interlocked.Increment(ref requestCount);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, CreateReleasePayload("v2.0.0", "app-v2.0.0.zip")));
        });

        var time = new FakeTimeProvider(BaseTime);
        var provider = CreateProvider(handler, time);

        var first = await provider.GetLatestReleaseAsync("owner/repo", "app-v*.zip");
        time.Advance(TimeSpan.FromMinutes(4));
        var second = await provider.GetLatestReleaseAsync("owner/repo", "app-v*.zip");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requestCount, Is.EqualTo(1));
            Assert.That(first?.TagName, Is.EqualTo("v2.0.0"));
            Assert.That(second?.TagName, Is.EqualTo("v2.0.0"));
        }
    }

    [Test]
    public async Task GetLatestReleaseAsync_ПослеИстеченияTtlИОтвета304_ОтправляетIfNoneMatchИВозвращаетКэшированныйРелиз()
    {
        var seenIfNoneMatch = new List<string?>();
        var callNumber = 0;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var current = Interlocked.Increment(ref callNumber);
            var ifNoneMatch = request.Headers.IfNoneMatch.FirstOrDefault()?.ToString();
            seenIfNoneMatch.Add(ifNoneMatch);

            if (current == 1)
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, CreateReleasePayload("v3.1.0", "app-v3.1.0.zip"), etag: "\"rel-310\""));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        });

        var time = new FakeTimeProvider(BaseTime);
        var provider = CreateProvider(handler, time);

        var first = await provider.GetLatestReleaseAsync("owner/repo", "app-v*.zip");
        time.Advance(TimeSpan.FromMinutes(6));
        var after304 = await provider.GetLatestReleaseAsync("owner/repo", "app-v*.zip");
        time.Advance(TimeSpan.FromMinutes(3));
        var cachedAfterRefresh = await provider.GetLatestReleaseAsync("owner/repo", "app-v*.zip");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(callNumber, Is.EqualTo(2));
            Assert.That(seenIfNoneMatch, Is.EqualTo([null, "\"rel-310\""]));
            Assert.That(first?.TagName, Is.EqualTo("v3.1.0"));
            Assert.That(after304?.TagName, Is.EqualTo("v3.1.0"));
            Assert.That(cachedAfterRefresh?.TagName, Is.EqualTo("v3.1.0"));
        }
    }

    [TestCase(HttpStatusCode.Forbidden, true, 12)]
    [TestCase(HttpStatusCode.TooManyRequests, false, 10)]
    public async Task GetLatestReleaseAsync_ПриОграниченииЛимита_ДержитПаузуИВозобновляетПослеСброса(
        HttpStatusCode limitStatus,
        bool includeResetHeader,
        int expectedMinutes)
    {
        var time = new FakeTimeProvider(BaseTime);
        var callNumber = 0;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var current = Interlocked.Increment(ref callNumber);

            if (current == 1)
            {
                var limited = new HttpResponseMessage(limitStatus);
                limited.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");

                if (includeResetHeader)
                {
                    var resetUnix = time.GetUtcNow().AddMinutes(expectedMinutes).ToUnixTimeSeconds();
                    limited.Headers.TryAddWithoutValidation("X-RateLimit-Reset", resetUnix.ToString());
                }

                return Task.FromResult(limited);
            }

            return Task.FromResult(JsonResponse(HttpStatusCode.OK, CreateReleasePayload("v4.0.0", "app-v4.0.0.zip")));
        });

        var provider = CreateProvider(handler, time);

        var first = await provider.GetLatestReleaseAsync("owner/first", "app-v*.zip");
        var blockedOtherRepo = await provider.GetLatestReleaseAsync("owner/second", "app-v*.zip");

        time.Advance(TimeSpan.FromMinutes(expectedMinutes - 1));
        var stillBlocked = await provider.GetLatestReleaseAsync("owner/second", "app-v*.zip");

        time.Advance(TimeSpan.FromMinutes(2));
        var afterCooldown = await provider.GetLatestReleaseAsync("owner/second", "app-v*.zip");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Null);
            Assert.That(blockedOtherRepo, Is.Null);
            Assert.That(stillBlocked, Is.Null);
            Assert.That(afterCooldown?.TagName, Is.EqualTo("v4.0.0"));
            Assert.That(callNumber, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetLatestReleaseAsync_ОтменаОдногоВызывающего_НеЛомаетКоалесцированныйЗапросСоседа()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 0;

        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            Interlocked.Increment(ref requestCount);
            started.TrySetResult();
            await releaseGate.Task.WaitAsync(ct);
            return JsonResponse(HttpStatusCode.OK, CreateReleasePayload("v5.0.0", "app-v5.0.0.zip"));
        });

        var provider = CreateProvider(handler, new FakeTimeProvider(BaseTime));

        using var cts = new CancellationTokenSource();
        var cancelledCaller = provider.GetLatestReleaseAsync("owner/repo", "app-v*.zip", cts.Token);
        await started.Task;

        var survivorCaller = provider.GetLatestReleaseAsync("owner/repo", "app-v*.zip");
        await cts.CancelAsync();

        Exception? cancelledException = null;
        try
        {
            await cancelledCaller;
        }
        catch (Exception ex)
        {
            cancelledException = ex;
        }

        releaseGate.SetResult();
        var survivorResult = await survivorCaller;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cancelledException, Is.InstanceOf<OperationCanceledException>());
            Assert.That(survivorResult?.TagName, Is.EqualTo("v5.0.0"));
            Assert.That(requestCount, Is.EqualTo(1));
        }
    }

    private static GitHubReleaseProvider CreateProvider(HttpMessageHandler handler, TimeProvider timeProvider)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("GitHub").Returns(_ => new HttpClient(handler, disposeHandler: false));

        return new(factory, NullLogger<GitHubReleaseProvider>.Instance, timeProvider);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object payload, string? etag = null)
    {
        var json = JsonSerializer.Serialize(payload);
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        if (etag is not null)
        {
            response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        }

        return response;
    }

    private static object CreateReleasePayload(string tagName, string assetName) =>
        new
        {
            tag_name = tagName,
            prerelease = false,
            published_at = BaseTime,
            body = "Release notes",
            assets = new[]
            {
                new
                {
                    name = assetName,
                    size = 1024L,
                    digest = "sha256:abc",
                    browser_download_url = $"https://example.test/{assetName}",
                },
            },
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }

    private sealed class FakeTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow += delta;
    }
}
