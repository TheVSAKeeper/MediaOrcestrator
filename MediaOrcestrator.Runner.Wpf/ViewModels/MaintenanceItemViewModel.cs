using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MaintenanceItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string? _sizeText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(IsResultVisible))]
    private MaintenanceItemState _state;

    [ObservableProperty]
    private string? _resultText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AgeHint))]
    private int _ageDays;

    public required string Key { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public bool IsDangerous { get; init; }

    public bool HasAgeFilter { get; init; }

    public int MinAgeDays { get; init; }

    public int MaxAgeDays { get; init; } = 365;

    public bool IsBusy => State == MaintenanceItemState.Running;

    public bool IsResultVisible => State is MaintenanceItemState.Success or MaintenanceItemState.Error;

    public string AgeHint => AgeDays <= 0
        ? "все файлы"
        : $"старше {AgeDays} {PluralizeDays(AgeDays)}";

    private static string PluralizeDays(int days)
    {
        var mod100 = days % 100;
        var mod10 = days % 10;

        if (mod100 is >= 11 and <= 14)
        {
            return "дней";
        }

        return mod10 == 1 ? "дня" : "дней";
    }
}

public enum MaintenanceItemState
{
    None = 0,
    Idle = 1,
    Running = 2,
    Success = 3,
    Error = 4,
}
