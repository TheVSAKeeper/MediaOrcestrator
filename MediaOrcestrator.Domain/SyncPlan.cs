namespace MediaOrcestrator.Domain;

public sealed class SyncPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<IntentObject> Intents { get; set; } = [];

    public Dictionary<string, List<IntentObject>> IntentsByRelation { get; set; } = new();
    public Dictionary<string, List<IntentObject>> IntentsByMedia { get; set; } = new();

    public int TotalCount => Intents.Count;
    public int SelectedCount => Intents.Count(x => x.Status == IntentStatus.Selected);
    public int CompletedCount => Intents.Count(x => x.Status == IntentStatus.Completed);
    public int FailedCount => Intents.Count(x => x.Status == IntentStatus.Failed);
}
