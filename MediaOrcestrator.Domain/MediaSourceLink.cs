using LiteDB;

namespace MediaOrcestrator.Domain;

/// <summary>
/// Статус медии в конкретном хранилище
/// </summary>
public class MediaSourceLink
{
    /// <summary>
    /// Идентификатор источника.
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? StatusMessage { get; set; }

    public int SortNumber { get; set; }

    /// <summary>
    /// Идентификатор медии в источнике.
    /// </summary>
    /// <remarks>
    /// В каждом источники свой айди.
    /// </remarks>
    public string ExternalId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Внутренний идентификатор медии.
    /// </summary>
    public string MediaId { get; set; } = string.Empty;

    [BsonIgnore]
    public Media Media { get; set; } = null!;

    public override string ToString()
    {
        return $"{SourceId}: {ExternalId} ({Status})";
    }
}
