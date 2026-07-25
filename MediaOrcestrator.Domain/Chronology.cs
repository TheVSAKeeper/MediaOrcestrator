using MediaOrcestrator.Modules;
using System.Globalization;
using System.Text;

namespace MediaOrcestrator.Domain;

public sealed record ChronologyItem(
    string MediaId,
    string SourceId,
    string ExternalId,
    string Title,
    string? Url,
    DateTimeOffset? PublishedAt);

public sealed class ChronologyPlan
{
    public required IReadOnlyList<ChronologyItem> Items { get; init; }

    public int MissingDateCount { get; init; }

    public string ToM3u()
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");

        foreach (var item in Items)
        {
            if (string.IsNullOrEmpty(item.Url))
            {
                continue;
            }

            sb.AppendLine($"#EXTINF:-1,{item.Title}");
            sb.AppendLine(item.Url);
        }

        return sb.ToString();
    }

    public string ToUrlList()
    {
        return string.Join(Environment.NewLine, Items.Where(i => !string.IsNullOrEmpty(i.Url)).Select(i => i.Url));
    }
}

public static class ChronologyPlanner
{
    public const string CreationDateKey = "CreationDate";

    public static ChronologyPlan Plan(
        IEnumerable<Media> medias,
        IReadOnlyCollection<string> sourceIds,
        Func<string, string, IReadOnlyList<MetadataItem>?, string?> urlResolver)
    {
        var set = new HashSet<string>(sourceIds, StringComparer.OrdinalIgnoreCase);
        var items = new List<ChronologyItem>();
        var missing = 0;

        foreach (var media in medias)
        {
            var link = media.Sources?.FirstOrDefault(s => set.Contains(s.SourceId));
            if (link is null)
            {
                continue;
            }

            var date = ExtractDate(media.Metadata, link.SourceId);
            if (date is null)
            {
                missing++;
            }

            var url = urlResolver(link.SourceId, link.ExternalId, media.Metadata);

            items.Add(new(media.Id, link.SourceId, link.ExternalId, link.Title ?? media.Title ?? string.Empty, url, date));
        }

        var ordered = items
            .OrderBy(i => i.PublishedAt is null)
            .ThenBy(i => i.PublishedAt ?? default)
            .ThenBy(i => i.ExternalId, StringComparer.Ordinal)
            .ToList();

        return new()
        {
            Items = ordered,
            MissingDateCount = missing,
        };
    }

    public static DateTimeOffset? ExtractDate(IReadOnlyList<MetadataItem>? metadata, string sourceId)
    {
        if (metadata is null)
        {
            return null;
        }

        var candidates = metadata
            .Where(m => m.Key == CreationDateKey && !string.IsNullOrWhiteSpace(m.Value))
            .ToList();

        var preferred = candidates.FirstOrDefault(m => m.SourceId == sourceId) ?? candidates.FirstOrDefault();
        if (preferred is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(preferred.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
