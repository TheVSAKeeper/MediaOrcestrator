using LiteDB;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Domain;

public class Source
{
    public string Id { get; set; } = string.Empty;

    public bool IsDisable { get; set; }

    public string TypeId { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = [];

    public DateTime? LastSyncedAt { get; set; }

    public string Title => Settings.GetValueOrDefault("_system_name", "<noname>");

    public string TitleFull => Title + " (" + TypeId + ")";

    [BsonIgnore]
    public ISourceType Type { get; set; } = null!;

    public override string ToString()
    {
        return TitleFull;
    }
}
