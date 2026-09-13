using CommunityToolkit.Mvvm.ComponentModel;
using MediaOrcestrator.Domain;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class BatchRenameRowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isApplyChecked = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChange))]
    private string _oldTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChange))]
    private string _newTitle = string.Empty;

    [ObservableProperty]
    private string _sourcesSummary = string.Empty;

    [ObservableProperty]
    private string _sourcesTooltip = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<OperationSourceChipViewModel> _sourceChips = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isOutOfSync;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _hasUpdatableSources;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isApplied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isApplyError;

    [ObservableProperty]
    private string? _resultTooltip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _previewError;

    public required Media Media { get; init; }

    public IReadOnlyList<BatchRenameSourcePreview> Sources { get; set; } = [];

    public bool IsActionable => IsApplyChecked
                                && PreviewError is null
                                && HasUpdatableSources
                                && IsOutOfSync;

    public bool HasChange => !string.Equals(OldTitle, NewTitle, StringComparison.Ordinal);

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

            return IsActionable ? OperationRowStatus.Pending : OperationRowStatus.Skipped;
        }
    }

    public string StatusText => Status switch
    {
        OperationRowStatus.Failed => "Ошибка",
        OperationRowStatus.Applied => "Применено",
        OperationRowStatus.Pending => "Ожидание",
        _ when PreviewError is not null => "Ошибка шаблона",
        _ when !HasUpdatableSources => "Нет площадок",
        _ when !IsApplyChecked => "Снято",
        _ => "Синхронно",
    };
}
