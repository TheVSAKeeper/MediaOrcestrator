using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class SourceCheckOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked = true;

    public required string SourceId { get; init; }

    public required string Title { get; init; }
}
