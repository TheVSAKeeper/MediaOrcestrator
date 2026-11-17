using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaOrcestrator.Domain;

public class GitHubReleaseProvider : IReleaseProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultRateLimitCooldown = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubReleaseProvider> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<object?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _rateLimitCooldownUntil = DateTimeOffset.MinValue;

    public GitHubReleaseProvider(IHttpClientFactory httpClientFactory, ILogger<GitHubReleaseProvider> logger)
        : this(httpClientFactory, logger, TimeProvider.System)
    {
    }

    internal GitHubReleaseProvider(IHttpClientFactory httpClientFactory, ILogger<GitHubReleaseProvider> logger, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<ReleaseInfo?> GetLatestReleaseAsync(string repo, string assetPattern, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var release = await FetchCachedJsonAsync<GitHubRelease>(
                $"https://api.github.com/repos/{repo}/releases/latest",
                cancellationToken);

            if (release is not null && !release.Assets.Any(a => GlobMatcher.IsMatch(a.Name, assetPattern)))
            {
                release = null;
            }

            if (release is null)
            {
                var releases = await FetchCachedJsonAsync<List<GitHubRelease>>(
                    $"https://api.github.com/repos/{repo}/releases",
                    cancellationToken);

                release = releases?.FirstOrDefault(r => !r.Prerelease && r.Assets.Any(a => GlobMatcher.IsMatch(a.Name, assetPattern)));
            }

            if (release is null)
            {
                _logger.LogWarning("Релизы не найдены для {Repo}", repo);
                return null;
            }

            var matchingAsset = release.Assets.FirstOrDefault(a => GlobMatcher.IsMatch(a.Name, assetPattern));

            return new()
            {
                TagName = release.TagName,
                AssetUrl = matchingAsset?.BrowserDownloadUrl,
                AssetName = matchingAsset?.Name,
                AssetSize = matchingAsset?.Size ?? 0,
                AssetDigest = matchingAsset?.Digest,
                PublishedAt = release.PublishedAt,
                Body = release.Body,
            };
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(ex, "Превышен лимит запросов GitHub API для {Repo}", repo);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Не удалось проверить релизы для {Repo}", repo);
            return null;
        }
    }

    public async Task DownloadAssetAsync(
        string url,
        string targetPath,
        string? expectedDigest = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("GitHub");

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var buffer = new byte[81920];
        long bytesRead = 0;

        await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = File.Create(targetPath))
        {
            int read;

            while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesRead += read;

                if (totalBytes > 0)
                {
                    progress?.Report((double)bytesRead / totalBytes);
                }
            }
        }

        try
        {
            if (!FileIntegrity.EnsureMatches(targetPath, expectedDigest))
            {
                _logger.LogWarning("Контрольная сумма для {Url} недоступна, файл принят без проверки", url);
            }
        }
        catch (InvalidDataException)
        {
            File.Delete(targetPath);
            throw;
        }
    }

    private async Task<T?> FetchCachedJsonAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task<object?> task;
        TaskCompletionSource<object?>? ownerTcs = null;
        CacheEntry? cachedForEtag = null;

        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();

            if (_cache.TryGetValue(url, out var entry) && now < entry.ExpiresAt)
            {
                return entry.Value as T;
            }

            if (now < _rateLimitCooldownUntil)
            {
                return null;
            }

            if (_inFlight.TryGetValue(url, out var existing))
            {
                task = existing;
            }
            else
            {
                ownerTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                task = ownerTcs.Task;
                _inFlight[url] = task;
                cachedForEtag = entry;
            }
        }

        if (ownerTcs is not null)
        {
            _ = CompleteFetchAsync<T>(url, cachedForEtag, ownerTcs);
        }

        var result = await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return result as T;
    }

    private async Task CompleteFetchAsync<T>(string url, CacheEntry? existingEntry, TaskCompletionSource<object?> tcs)
        where T : class
    {
        object? fetched = null;
        Exception? failure = null;

        try
        {
            fetched = await FetchFromNetworkAsync<T>(url, existingEntry).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            lock (_sync)
            {
                if (_inFlight.TryGetValue(url, out var current) && ReferenceEquals(current, tcs.Task))
                {
                    _inFlight.Remove(url);
                }
            }
        }

        if (failure is not null)
        {
            tcs.TrySetException(failure);
        }
        else
        {
            tcs.TrySetResult(fetched);
        }
    }

    private async Task<object?> FetchFromNetworkAsync<T>(string url, CacheEntry? existingEntry)
        where T : class
    {
        var client = _httpClientFactory.CreateClient("GitHub");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (!string.IsNullOrWhiteSpace(existingEntry?.ETag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", existingEntry.ETag);
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Не удалось запросить GitHub API: {Url}", url);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Таймаут запроса GitHub API: {Url}", url);
            return null;
        }

        using (response)
        {
            var now = _timeProvider.GetUtcNow();

            if (response.StatusCode == HttpStatusCode.NotModified && existingEntry is not null)
            {
                var updatedEtag = ExtractETag(response) ?? existingEntry.ETag;

                lock (_sync)
                {
                    _cache[url] = new(existingEntry.Value, updatedEtag, now + CacheTtl);
                }

                return existingEntry.Value;
            }

            if (IsRateLimited(response))
            {
                var cooldownUntil = ComputeCooldownUntil(response, now);

                lock (_sync)
                {
                    if (cooldownUntil > _rateLimitCooldownUntil)
                    {
                        _rateLimitCooldownUntil = cooldownUntil;
                    }
                }

                _logger.LogWarning("Превышен лимит запросов GitHub API для {Url}, пауза до {CooldownUntil}", url, cooldownUntil);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var parsed = await response.Content.ReadFromJsonAsync<T>(JsonOptions, CancellationToken.None).ConfigureAwait(false);
            var etag = ExtractETag(response);

            lock (_sync)
            {
                _cache[url] = new(parsed, etag, now + CacheTtl);
            }

            return parsed;
        }
    }

    private static string? ExtractETag(HttpResponseMessage response)
    {
        if (response.Headers.ETag is not null)
        {
            return response.Headers.ETag.ToString();
        }

        return response.Headers.TryGetValues("ETag", out var values) ? values.FirstOrDefault() : null;
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (response.StatusCode != HttpStatusCode.Forbidden)
        {
            return false;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues)
            && int.TryParse(remainingValues.FirstOrDefault(), out var remaining)
            && remaining > 0
            && response.Headers.RetryAfter is null)
        {
            return false;
        }

        return true;
    }

    private static DateTimeOffset ComputeCooldownUntil(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
            && long.TryParse(resetValues.FirstOrDefault(), out var resetSeconds))
        {
            var resetAt = DateTimeOffset.FromUnixTimeSeconds(resetSeconds);

            if (resetAt > now)
            {
                return resetAt;
            }
        }

        if (response.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                return now + delta;
            }

            if (retryAfter.Date is { } date && date > now)
            {
                return date;
            }
        }

        return now + DefaultRateLimitCooldown;
    }

    private sealed record CacheEntry(object? Value, string? ETag, DateTimeOffset ExpiresAt);

    private sealed record GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("assets")]
        public List<GithubAsset> Assets { get; init; } = [];
    }

    private sealed record GithubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;
    }
}
