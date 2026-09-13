using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class SourceCardViewModel : ObservableObject
{
    private readonly SourcesRelationsViewModel _owner;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    public SourceCardViewModel(Source source, SourcesRelationsViewModel owner, SourceCardStats stats)
    {
        Source = source;
        _owner = owner;
        Stats = stats;
    }

    public Source Source { get; }

    public SourceCardStats Stats { get; }

    public string Title => string.IsNullOrWhiteSpace(Source.Title) ? "<без имени>" : Source.Title;

    public string TypeId => Source.TypeId;

    public bool IsDisabled => Source.IsDisable;

    public PackIconLucideKind DisableIcon => Source.IsDisable ? PackIconLucideKind.Play : PackIconLucideKind.Pause;

    public string DisableTooltip => Source.IsDisable ? "Включить" : "Выключить";

    [RelayCommand]
    private Task Edit()
    {
        return _owner.EditSource(this);
    }

    [RelayCommand]
    private Task Duplicate()
    {
        return _owner.DuplicateSource(this);
    }

    [RelayCommand]
    private void ToggleDisable()
    {
        _owner.ToggleSourceDisable(this);
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
        _owner.DeleteSource(this);
    }
}
