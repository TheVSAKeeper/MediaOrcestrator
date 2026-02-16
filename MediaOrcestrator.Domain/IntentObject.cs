using LiteDB;
using System.Text;

namespace MediaOrcestrator.Domain;

public sealed class IntentObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public IntentType Type { get; set; }
    public string MediaId { get; set; } = string.Empty;
    public string? SourceId { get; set; }
    public string? TargetId { get; set; }
    public string? ExternalId { get; set; }
    public IntentStatus Status { get; set; } = IntentStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExecutedAt { get; set; }

    [BsonIgnore]
    public StringBuilder LogOutput { get; set; } = new();

    [BsonIgnore]
    public Media? Media { get; set; }

    [BsonIgnore]
    public Source? Source { get; set; }

    [BsonIgnore]
    public Source? Target { get; set; }

    [BsonIgnore]
    public List<IntentObject> Dependencies { get; set; } = [];
}

public enum IntentType
{
    None = 0,
    Download = 1,
    Upload = 2,
    UpdateStatus = 3,
    MarkAsDeleted = 4,
}

public enum IntentStatus
{
    None = 0,
    Pending = 1,
    Selected = 2,
    Running = 3,
    Completed = 4,
    Failed = 5,
    Skipped = 6,
}
