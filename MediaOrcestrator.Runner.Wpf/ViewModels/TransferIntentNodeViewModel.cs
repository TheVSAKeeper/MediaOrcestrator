using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class TransferIntentNodeViewModel : ObservableObject
{
    private readonly TransfersViewModel _owner;
    private bool _isSelected;

    [ObservableProperty]
    private TransferStatusKind _statusKind;

    [ObservableProperty]
    private string _statusTail = string.Empty;

    [ObservableProperty]
    private string? _errorTooltip;

    [ObservableProperty]
    private bool _isExpanded = true;

    public TransferIntentNodeViewModel(TransfersViewModel owner, SyncIntent intent, TransferIntentNodeViewModel? parent)
    {
        _owner = owner;
        Intent = intent;
        Parent = parent;
        _isSelected = intent.IsSelected;
        Children = [];
    }

    public SyncIntent Intent { get; }

    public TransferIntentNodeViewModel? Parent { get; }

    public RangeObservableCollection<TransferIntentNodeViewModel> Children { get; }

    public string Route => $"{Intent.From.TitleFull} → {Intent.To.TitleFull}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            SetIsSelectedSilent(value);
            _owner.OnNodeSelectionChanged(this, value);
        }
    }

    internal void SetIsSelectedSilent(bool value)
    {
        if (_isSelected == value)
        {
            return;
        }

        _isSelected = value;
        Intent.IsSelected = value;
        OnPropertyChanged(nameof(IsSelected));
    }

    internal void ResetRunState()
    {
        StatusKind = TransferStatusKind.Pending;
        StatusTail = string.Empty;
        ErrorTooltip = null;
    }

    [RelayCommand]
    private Task RunSingleStep() => _owner.RunSingleStepAsync(this);

    [RelayCommand]
    private void OpenInMedia() => _owner.OpenInMedia(Intent.Media);

    [RelayCommand]
    private void ShowInGraph() => _owner.OpenSourcesGraph(Intent.Relation);

    [RelayCommand]
    private void SelectChain()
    {
        IsSelected = true;
        SetSelfAndDescendantsTrue(this);
    }

    [RelayCommand]
    private void DeselectChain()
    {
        IsSelected = false;
    }

    private static void SetSelfAndDescendantsTrue(TransferIntentNodeViewModel node)
    {
        foreach (var child in node.Children)
        {
            child.IsSelected = true;
            SetSelfAndDescendantsTrue(child);
        }
    }

    internal void ApplyStatus(SyncAttemptStatus status)
    {
        switch (status.Kind)
        {
            case SyncAttemptKind.Started:
                StatusKind = TransferStatusKind.Working;
                StatusTail = status.AttemptNumber > 1
                    ? $"[В работе, попытка {status.AttemptNumber}/{status.MaxAttempts}]"
                    : "[В работе]";
                ErrorTooltip = null;
                break;

            case SyncAttemptKind.Succeeded:
                StatusKind = TransferStatusKind.Ok;
                StatusTail = "(Готово)";
                ErrorTooltip = null;
                break;

            case SyncAttemptKind.FailedRetrying:
                StatusKind = TransferStatusKind.Waiting;
                var minutes = status.NextDelay?.TotalMinutes ?? 0;
                StatusTail = $"[Ожидание {minutes:F0} мин перед попыткой {status.AttemptNumber + 1}/{status.MaxAttempts}]";
                ErrorTooltip = status.Error?.Message;
                break;

            case SyncAttemptKind.FailedFinal:
                StatusKind = TransferStatusKind.Error;
                StatusTail = "[Ошибка]";
                ErrorTooltip = status.Error?.Message;
                break;

            case SyncAttemptKind.NonRetriable:
                StatusKind = TransferStatusKind.NonRetriable;
                StatusTail = "[Не ретраим]";
                ErrorTooltip = status.Error?.Message;
                break;

            case SyncAttemptKind.Joined:
                StatusKind = TransferStatusKind.Joined;
                StatusTail = "[Ожидание: уже выполняется]";
                ErrorTooltip = null;
                break;

            case SyncAttemptKind.Skipped:
                StatusKind = TransferStatusKind.Skipped;
                StatusTail = "[Пропущено]";
                ErrorTooltip = status.Error?.Message;
                break;
        }
    }
}
