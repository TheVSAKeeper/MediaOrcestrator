namespace MediaOrcestrator.Domain;

/// <summary>
/// Синхронизационное намеренье.
/// </summary>
public sealed class SyncIntent
{
    public required Media Media { get; set; }
    public required Source From { get; set; }
    public required Source To { get; set; }
    public required SourceSyncRelation Relation { get; set; }

    public List<SyncIntent> NextIntents { get; set; } = [];

    public bool IsSelected { get; set; } = true;
    public int Sort { get; set; }

    public override string ToString()
    {
        return $"{Media.Title}: {From.TypeId} -> {To.TypeId}";
    }
}
