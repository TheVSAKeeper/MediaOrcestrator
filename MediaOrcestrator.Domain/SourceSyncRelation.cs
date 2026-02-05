using LiteDB;

namespace MediaOrcestrator.Domain;

/// <summary>
/// Связи синхронизациимежду хранилищами
/// </summary>
public class SourceSyncRelation
{
    public bool IsDisable { get; set; }

    public string FromId { get; set; }

    public string ToId { get; set; }

    [BsonIgnore]
    public Source To { get; set; }

    [BsonIgnore]
    public Source From { get; set; }

    public override string ToString()
    {
        return $"{From} -> {To}";
    }
}
