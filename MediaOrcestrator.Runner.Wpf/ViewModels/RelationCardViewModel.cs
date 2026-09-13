using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class RelationCardViewModel : ObservableObject
{
    private readonly SourcesRelationsViewModel _owner;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    public RelationCardViewModel(SourceSyncRelation relation, SourcesRelationsViewModel owner, RelationCardStats stats)
    {
        Relation = relation;
        _owner = owner;
        Stats = stats;
    }

    public SourceSyncRelation Relation { get; }

    public RelationCardStats Stats { get; }

    public string FromTitle => Relation.From?.Title ?? "–";

    public string ToTitle => Relation.To?.Title ?? "–";

    public string FromTypeId => Relation.From?.TypeId ?? string.Empty;

    public string ToTypeId => Relation.To?.TypeId ?? string.Empty;

    public bool IsDisabled => Relation.IsDisable;

    public string DisabledHint => Relation.IsDisable
        ? "связь выключена (один из источников отключён)"
        : string.Empty;

    [RelayCommand]
    private void Invert()
    {
        _owner.InvertRelation(this);
    }

    [RelayCommand]
    private void RequestDelete()
    {
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        IsConfirmingDelete = false;
    }

    [RelayCommand]
    private void ConfirmDelete()
    {
        IsConfirmingDelete = false;
        _owner.DeleteRelation(this);
    }
}
