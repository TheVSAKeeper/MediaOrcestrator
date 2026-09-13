using MediaOrcestrator.Domain;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed record MetadataColumnInfo(string ColumnId, string Key, string? SourceId, string DisplayName, string? DisplayType)
{
    public string? GroupName { get; init; }

    public string ShortDisplayName { get; init; } = string.Empty;
}

internal static class MediaMetadataColumns
{
    public static List<MetadataColumnInfo> Build(IReadOnlyList<Media> medias, IReadOnlyList<Source> sources)
    {
        var sourceNameMap = sources.ToDictionary(s => s.Id, s => s.Title);

        var pairs = medias
            .SelectMany(m => m.Metadata ?? [])
            .Where(m => m.SourceId != null && !IsSystemKey(m.Key))
            .GroupBy(m => (m.Key, m.SourceId))
            .Select(g => g.First())
            .ToList();

        var duplicateKeys = pairs
            .GroupBy(m => m.Key)
            .Where(g => g.Select(m => m.SourceId).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        var result = new List<MetadataColumnInfo>();

        foreach (var group in pairs.GroupBy(m => m.Key).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            if (duplicateKeys.Contains(group.Key))
            {
                foreach (var meta in group.OrderBy(m => m.SourceId, StringComparer.OrdinalIgnoreCase))
                {
                    var sourceName = sourceNameMap.GetValueOrDefault(meta.SourceId!, meta.SourceId!);
                    var shortName = meta.DisplayName ?? meta.Key;
                    var displayName = $"{shortName} ({sourceName})";
                    var columnId = $"{meta.Key} ({sourceName})";
                    result.Add(new(columnId, meta.Key, meta.SourceId, displayName, meta.DisplayType)
                    {
                        GroupName = sourceName,
                        ShortDisplayName = shortName,
                    });
                }
            }
            else
            {
                var meta = group.First();
                var shortName = meta.DisplayName ?? meta.Key;
                var sourceName = meta.SourceId != null
                    ? sourceNameMap.GetValueOrDefault(meta.SourceId, meta.SourceId)
                    : null;
                result.Add(new(meta.Key, meta.Key, null, shortName, meta.DisplayType)
                {
                    GroupName = sourceName,
                    ShortDisplayName = shortName,
                });
            }
        }

        return result;
    }

    public static string Resolve(Media media, MetadataColumnInfo column)
    {
        var metadata = media.Metadata;

        if (metadata is null)
        {
            return string.Empty;
        }

        var item = column.SourceId != null
            ? metadata.FirstOrDefault(m => m.Key == column.Key && m.SourceId == column.SourceId)
            : metadata.FirstOrDefault(m => m.Key == column.Key);

        return item is null ? string.Empty : MetadataValueFormatter.Format(item.Value, item.DisplayType);
    }

    private static bool IsSystemKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        return key.StartsWith("_system", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "PreviewUrl", StringComparison.OrdinalIgnoreCase);
    }
}
