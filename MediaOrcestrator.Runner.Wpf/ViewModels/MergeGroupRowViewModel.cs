using CommunityToolkit.Mvvm.ComponentModel;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Merging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MergeGroupRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isMergeChecked = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private bool _isApplied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private bool _isApplyError;

    [ObservableProperty]
    private string? _resultText;

    [ObservableProperty]
    private string? _resultTooltip;

    public required MergeCandidateGroup Group { get; init; }

    public OperationRowStatus Status => IsApplyError
        ? OperationRowStatus.Failed
        : IsApplied
            ? OperationRowStatus.Applied
            : OperationRowStatus.Pending;

    public required string TargetTitle { get; init; }

    public required string MergingMembers { get; init; }

    public required int TotalSourcesCount { get; init; }

    public string NormalizedKey => Group.NormalizedKey;

    public int MediaCount => Group.Medias.Count;

    public int ToBeRemoved => Math.Max(Group.Medias.Count - 1, 0);
}
