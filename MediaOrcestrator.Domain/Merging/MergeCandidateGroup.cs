namespace MediaOrcestrator.Domain.Merging;

public sealed class MergeCandidateGroup
{
    public required string NormalizedKey { get; init; }

    public required IReadOnlyList<Media> Medias { get; init; }

    public required Media SuggestedTarget { get; init; }

    public int TotalMediaCount => Medias.Count;
}
