namespace MediaOrcestrator.Domain.Merging;

public sealed class MergePreview
{
    public required Media TargetMedia { get; init; }

    public required IReadOnlyList<Media> SourceMedias { get; init; }

    public required IReadOnlyList<MediaSourceLink> ResultingSources { get; init; }

    public required IReadOnlyList<string> Conflicts { get; init; }

    public bool HasConflicts => Conflicts.Count > 0;

    public int TotalSourcesCount => ResultingSources.Count;
}
