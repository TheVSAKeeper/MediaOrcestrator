using LiteDB;

namespace MediaOrcestrator.Domain;

/// <summary>
/// Связи синхронизации между хранилищами
/// </summary>
public class SourceSyncRelation
{
    public bool IsDisable { get; set; }

    public string FromId { get; set; } = string.Empty;

    public string ToId { get; set; } = string.Empty;

    [BsonIgnore]
    public Source To { get; set; } = null!;

    [BsonIgnore]
    public Source From { get; set; } = null!;

    public override string ToString()
    {
        return $"{From} -> {To}";
    }
}
