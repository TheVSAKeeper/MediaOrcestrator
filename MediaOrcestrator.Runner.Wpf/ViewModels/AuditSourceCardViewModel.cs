using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class AuditSourceCardViewModel : ObservableObject
{
    private readonly AuditViewModel _owner;

    [ObservableProperty]
    private string _progressMessage = "–";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncFullCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncQuickCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncNewCommand))]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncFullCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncQuickCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncNewCommand))]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private bool _isBulkLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    private bool _hasError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    private bool _isCancelled;

    [ObservableProperty]
    private string _lastSyncText = string.Empty;

    public AuditSourceCardViewModel(Source source, AuditViewModel owner)
    {
        Source = source;
        _owner = owner;
        LastSyncText = FormatLastSync(source.LastSyncedAt);
    }

    public Source Source { get; }

    public string SourceId => Source.Id;

    public string Title => string.IsNullOrWhiteSpace(Source.Title) ? Source.TitleFull : Source.Title;

    public string TypeId => Source.TypeId;

    public bool IsDisabled => Source.IsDisable;

    public bool CanRunSync => !IsBusy && !IsBulkLocked && !IsDisabled;

    public bool IsActive => IsBusy || IsBulkLocked;

    public bool HasProgress => IsActive || HasError || IsCancelled;

    public void Report(string message)
    {
        HasError = false;
        IsCancelled = false;
        ProgressMessage = message;
    }

    public void ReportError(string message)
    {
        IsCancelled = false;
        ProgressMessage = message;
        HasError = true;
    }

    public void ReportCancelled(string message)
    {
        HasError = false;
        ProgressMessage = message;
        IsCancelled = true;
    }

    public void ResetState()
    {
        HasError = false;
        IsCancelled = false;
    }

    public void Reset()
    {
        HasError = false;
        IsCancelled = false;
        ProgressMessage = "–";
    }

    public void ClearProgress()
    {
        HasError = false;
        IsCancelled = false;
        ProgressMessage = "–";
    }

    public void RefreshFromSource()
    {
        LastSyncText = FormatLastSync(Source.LastSyncedAt);
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRunSync))]
    private Task SyncFull(CancellationToken cancellationToken)
    {
        return _owner.RunForSourceAsync(this, AuditSyncMode.Full, cancellationToken);
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRunSync))]
    private Task SyncQuick(CancellationToken cancellationToken)
    {
        return _owner.RunForSourceAsync(this, AuditSyncMode.Quick, cancellationToken);
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRunSync))]
    private Task SyncNew(CancellationToken cancellationToken)
    {
        return _owner.RunForSourceAsync(this, AuditSyncMode.New, cancellationToken);
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunSync));
    }

    partial void OnIsBulkLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunSync));
    }

    private static string FormatLastSync(DateTime? value)
    {
        if (value is null)
        {
            return "не синхронизировано";
        }

        return $"синхр.: {value.Value.ToLocalTime():dd.MM.yyyy HH:mm}";
    }
}
