using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Domain;

public class SourceRelation
{
    public string IdFrom { get; set; }
    public string IdTo { get; set; }
    public IMediaSource? To { get; set; }
    public IMediaSource? From { get; set; }
}
