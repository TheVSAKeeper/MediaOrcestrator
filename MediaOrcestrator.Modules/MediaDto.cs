namespace MediaOrcestrator.Modules;

public class MediaDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DataPath { get; set; } = string.Empty;
    public string PreviewPath { get; set; } = string.Empty;

    public List<MetadataItem>? Metadata { get; set; }

    public string TempDataPath { get; set; } = string.Empty; // todo это отсюда уберём
    public string TempPreviewPath { get; set; } = string.Empty;
}
