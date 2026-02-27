namespace MediaOrcestrator.Modules;

public class MetadataItem
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public string? DisplayName { get; set; }
    public string? DisplayType { get; set; }
}
