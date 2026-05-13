namespace MediaOrcestrator.Modules;

public sealed class MetadataItem
{
    public string? SourceId { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public string? DisplayName { get; set; }
    public string? DisplayType { get; set; }
}

public static class MetadataExtensions
{
    public static IReadOnlyList<MetadataItem>? ForSource(this IReadOnlyList<MetadataItem>? metadata, string sourceId)
    {
        if (metadata == null)
        {
            return null;
        }

        var filtered = new List<MetadataItem>(metadata.Count);
        foreach (var item in metadata)
        {
            if (item.SourceId == null || item.SourceId == sourceId)
            {
                filtered.Add(item);
            }
        }

        return filtered;
    }
}
