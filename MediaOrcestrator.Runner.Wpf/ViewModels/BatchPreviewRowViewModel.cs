using CommunityToolkit.Mvvm.ComponentModel;
using MediaOrcestrator.Domain;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class BatchPreviewRowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private bool _isApplyChecked = true;

    [ObservableProperty]
    private string _statusText = "Ожидание";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private bool _isApplied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private bool _isApplyError;

    [ObservableProperty]
    private string? _resultTooltip;

    public required Media Media { get; init; }

    public required Source Target { get; init; }

    public string MediaTitle => Media.Title ?? string.Empty;

    public string TargetTitle => Target.TitleFull;

    public OperationRowStatus Status
    {
        get
        {
            if (IsApplyError)
            {
                return OperationRowStatus.Failed;
            }

            if (IsApplied)
            {
                return OperationRowStatus.Applied;
            }

            return IsApplyChecked ? OperationRowStatus.Pending : OperationRowStatus.Skipped;
        }
    }
}
