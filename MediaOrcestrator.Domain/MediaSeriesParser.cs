using MediaOrcestrator.Domain.Merging;
using System.Text.RegularExpressions;

namespace MediaOrcestrator.Domain;

public sealed record MediaSeriesInfo(string RawSeriesName, string SeriesKey, int? EpisodeNumber, string EpisodeTitle, bool HasSeries);

public sealed record MediaSeriesEntry(string SeriesName, string SeriesKey, int? EpisodeNumber, bool IsNoSeries);

public static class MediaSeriesParser
{
    public const string NoSeriesName = "Без серии";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly string[] Separators = [" / ", " — ", " – ", " | "];

    public static MediaSeriesInfo Parse(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return new(string.Empty, string.Empty, null, string.Empty, false);
        }

        foreach (var separator in Separators)
        {
            var index = trimmed.IndexOf(separator, StringComparison.Ordinal);

            if (index <= 0)
            {
                continue;
            }

            var rawSeries = trimmed[..index].Trim();
            var episodePart = trimmed[(index + separator.Length)..].Trim();

            if (rawSeries.Length == 0)
            {
                continue;
            }

            var key = MergeCandidateFinder.Normalize(rawSeries);

            if (key.Length == 0)
            {
                continue;
            }

            return new(rawSeries, key, ExtractEpisode(episodePart), episodePart, true);
        }

        return new(string.Empty, string.Empty, ExtractEpisode(trimmed), trimmed, false);
    }

    public static IReadOnlyList<MediaSeriesEntry> Assign(IReadOnlyList<Media> medias)
    {
        var infos = new MediaSeriesInfo[medias.Count];
        var rawNamesByKey = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        for (var i = 0; i < medias.Count; i++)
        {
            var info = Parse(medias[i].Title);
            infos[i] = info;

            if (!info.HasSeries)
            {
                continue;
            }

            if (!rawNamesByKey.TryGetValue(info.SeriesKey, out var counts))
            {
                counts = new(StringComparer.Ordinal);
                rawNamesByKey[info.SeriesKey] = counts;
            }

            counts[info.RawSeriesName] = counts.GetValueOrDefault(info.RawSeriesName) + 1;
        }

        var canonicalByKey = new Dictionary<string, string>(rawNamesByKey.Count, StringComparer.Ordinal);

        foreach (var (key, counts) in rawNamesByKey)
        {
            canonicalByKey[key] = counts
                .OrderByDescending(p => p.Value)
                .ThenByDescending(p => p.Key.Length)
                .ThenBy(p => p.Key, StringComparer.Ordinal)
                .First()
                .Key;
        }

        var result = new MediaSeriesEntry[medias.Count];

        for (var i = 0; i < medias.Count; i++)
        {
            var info = infos[i];

            result[i] = info.HasSeries
                ? new(canonicalByKey[info.SeriesKey], info.SeriesKey, info.EpisodeNumber, false)
                : new(NoSeriesName, string.Empty, info.EpisodeNumber, true);
        }

        return result;
    }

    private static int? ExtractEpisode(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            var match = Regex.Match(text, @"#(\d+)", RegexOptions.None, RegexTimeout);

            if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
            {
                return number;
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }

        return null;
    }
}
