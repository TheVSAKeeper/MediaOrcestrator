using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Domain;

public class Media
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public List<MetadataItem> Metadata { get; set; } = [];

    public List<MediaSourceLink> Sources { get; set; } = [];

    public override string ToString()
    {
        return $"{Title} (ID: {Id})";
    }
}
