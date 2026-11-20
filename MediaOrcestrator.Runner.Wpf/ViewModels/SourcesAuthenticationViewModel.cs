using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class SourcesAuthenticationViewModel(
    SourceAuthenticationService authentication,
    Func<SourceAuthenticationPromptContext, CancellationToken, Action<string>, IAuthUI> createAuthUi,
    ILogger<SourcesAuthenticationViewModel> logger,
    IDialogService dialogs,
    ISettingsStore? settings = null) : ObservableObject
{
    private const string AttentionPreference = "wpf.sources.auth.attention_only";
    private static readonly SourceAuthenticationStatus[] SummaryOrder =
    [
        SourceAuthenticationStatus.Valid,
        SourceAuthenticationStatus.Rejected,
        SourceAuthenticationStatus.CheckFailed,
        SourceAuthenticationStatus.SettingsRequired,
        SourceAuthenticationStatus.Saved,
        SourceAuthenticationStatus.Missing,
        SourceAuthenticationStatus.None,
    ];
    private List<SourceAuthenticationRowViewModel> _rows = [];
    private SourceAuthenticationRowViewModel? _signingIn;
    private SourceAuthenticationRowViewModel? _expandedRow;
    private SourceAuthenticationRowViewModel? _queueRow;
    private Action? _returnToOperation;

    public ObservableCollection<SourceAuthenticationRowViewModel> VisibleRows { get; } = [];
    public bool HasSources => _rows.Count > 0;
    public bool IsChecking => _checks.Count > 0;
    public bool CanOpenSourceSettings => _signingIn is null && !_copyingAuthentication && _editing.Count == 0;
    public bool IsSigningIn => _signingIn is not null;
    public bool IsEmpty => VisibleRows.Count == 0;
    public bool CanCloseDetails => _expandedRow is not null;
    public IReadOnlyList<AuthenticationSummaryChip> Summary { get; } = SummaryOrder
        .Select(status => new AuthenticationSummaryChip(status, SummaryLabel(status))).ToArray();

    public bool AllSourcesDisabled => HasSources && _rows.All(row => row.IsDisabled);
    public string CompactSummaryTitle => StatusFilter is { } status ? $"Сводка · {SummaryLabel(status)}" : "Сводка";

    public string EmptyText => IsCatalogLoading
        ? "Загружаем источники…"
        : CatalogError is not null ? "Обновите список, чтобы повторить загрузку."
        : StatusFilter is not null ? "Нет источников с выбранным состоянием."
        : (HasSources
            ? _rows.Any(row => !row.IsDisabled && row.IsUnchecked)
                ? "Пока нет источников, требующих внимания. Проверка ещё не завершена."
                : "Нет включённых источников, требующих внимания."
            : "Нет источников с авторизацией.");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private bool _isCatalogLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private string? _catalogError;

    [ObservableProperty]
    private bool _attentionOnly = bool.TryParse(settings?.GetStringValue(AttentionPreference), out var filtered) && filtered;

    [ObservableProperty]
    private string? _progressText;

    [ObservableProperty]
    private SourceAuthenticationRowViewModel? _selectedRow;

    [ObservableProperty]
    private string? _returnText;

    [ObservableProperty]
    private SourceAuthenticationStatus? _statusFilter;

    internal void SetSources(IEnumerable<Source> sources)
    {
        var previous = _rows.ToDictionary(row => row.Source.Id);
        var selectedId = SelectedRow?.Source.Id;
        var expandedId = _expandedRow?.Source.Id;
        _rows = sources.Where(source => source.Type is IAuthenticatable or ISourceAuthenticationProbe)
            .OrderBy(source => source.IsDisable)
            .ThenBy(source => source.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(source =>
            {
                var state = authentication.ReadState(source);
                if (previous.TryGetValue(source.Id, out var row) && row.State.Revision == state.Revision)
                {
                    previous.Remove(source.Id);
                    var unchanged = state.CheckedAt == row.State.CheckedAt;
                    row.Refresh(source, unchanged ? row.State : state);
                    return row;
                }

                return new SourceAuthenticationRowViewModel(source, state);
            })
            .ToList();

        foreach (var removed in previous.Values)
        {
            CancelCheck(removed.Source.Id);
        }

        AdvanceUnavailableSteps();
        NotifyResults();
        SelectedRow = selectedId is null ? null : FindRow(selectedId);
        SetExpanded(expandedId is null ? null : FindRow(expandedId));
        ScheduleChecks();
    }

    [RelayCommand]
    private void ToggleDetails(SourceAuthenticationRowViewModel row)
    {
        SelectedRow = row;
        SetExpanded(row == _expandedRow ? null : row);
    }

    internal bool CloseDetails()
    {
        if (_expandedRow is null)
        {
            return false;
        }

        SetExpanded(null);
        return true;
    }

    private void SetExpanded(SourceAuthenticationRowViewModel? row)
    {
        if (_expandedRow == row)
        {
            return;
        }

        _expandedRow?.IsExpanded = false;
        _expandedRow = row;
        _expandedRow?.IsExpanded = true;
        OnPropertyChanged(nameof(CanCloseDetails));
    }

    internal void SetReturnLink(string title, Action navigate)
    {
        _returnToOperation = navigate;
        ReturnText = $"Вернуться · {title}";
    }

    [RelayCommand]
    private void ReturnToOperation()
    {
        var navigate = _returnToOperation;
        _returnToOperation = null;
        ReturnText = null;
        navigate?.Invoke();
    }

    internal async Task FocusSourceAsync(SourceOperationAuthenticationException failure)
    {
        if (FindRow(failure.SourceId) is not { } row)
        {
            return;
        }

        AttentionOnly = false;
        StatusFilter = null;
        var sameRevision = !string.IsNullOrEmpty(failure.Revision) && row.State.Revision == failure.Revision;
        if (sameRevision && !row.IsAwaiting(failure.MethodId) && !(row.State.CheckedAt > failure.ObservedAt))
        {
            CancelCheck(failure.SourceId);
            var state = await authentication.RecordOperationRejectionAsync(row.Source, failure);
            if (_rows.Contains(row) && state is not null)
            {
                row.Refresh(row.Source, state);
            }
        }
        if (FindRow(failure.SourceId) is not { } latest)
        {
            return;
        }
        SelectedRow = latest;
        SetExpanded(latest);
        NotifyResults();
    }

    private SourceAuthenticationRowViewModel? FindRow(string sourceId) => _rows.Find(row => row.Source.Id == sourceId);

    partial void OnAttentionOnlyChanged(bool value)
    {
        settings?.SetValue(AttentionPreference, value.ToString());
        if (value)
        {
            StatusFilter = null;
        }
        NotifyResults();
    }

    partial void OnStatusFilterChanged(SourceAuthenticationStatus? value)
    {
        OnPropertyChanged(nameof(CompactSummaryTitle));
        NotifyResults();
    }

    [RelayCommand]
    private void FilterByStatus(AuthenticationSummaryChip chip)
    {
        AttentionOnly = false;
        StatusFilter = StatusFilter == chip.Status ? null : chip.Status;
    }

    private void NotifyResults()
    {
        RefreshVisibleRows();
        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(IsChecking));
        var counts = _rows.Where(row => !row.IsDisabled).CountBy(GetSummaryCategory).ToDictionary();
        foreach (var chip in Summary)
        {
            chip.IsSelected = StatusFilter == chip.Status;
            chip.Count = counts.GetValueOrDefault(chip.Status);
        }
        OnPropertyChanged(nameof(AllSourcesDisabled));
        OnPropertyChanged(nameof(AttentionCount));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(SignInCount));
        OnPropertyChanged(nameof(TabText));
        OnPropertyChanged(nameof(QueueButtonText));
        OnPropertyChanged(nameof(QueuePreview));
        NotifyQueue();
    }

    private void RefreshVisibleRows()
    {
        var current = QueueCurrent;
        var visible = _rows.Where(row => row == current || (!AttentionOnly || row.NeedsAttention)
            && (StatusFilter is null || !row.IsDisabled && GetSummaryCategory(row) == StatusFilter))
            .OrderBy(row => row != current).ToArray();
        var membership = visible.ToHashSet();
        for (var index = VisibleRows.Count - 1; index >= 0; index--)
        {
            if (!membership.Contains(VisibleRows[index]))
            {
                VisibleRows.RemoveAt(index);
            }
        }

        for (var index = 0; index < visible.Length; index++)
        {
            var currentIndex = VisibleRows.IndexOf(visible[index]);
            if (currentIndex < 0)
            {
                VisibleRows.Insert(index, visible[index]);
            }
            else if (currentIndex != index)
            {
                VisibleRows.Move(currentIndex, index);
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private void NotifyQueue()
    {
        if (_queueRow != QueueCurrent)
        {
            _queueRow?.IsQueueCurrent = false;
            _queueRow = QueueCurrent;
            _queueRow?.IsQueueCurrent = true;
            RefreshVisibleRows();
            if (_queueRow is not null)
            {
                SelectedRow = _queueRow;
            }
        }
        OnPropertyChanged(nameof(QueueCurrent));
        OnPropertyChanged(nameof(HasQueue));
        OnPropertyChanged(nameof(QueueText));
        OnPropertyChanged(nameof(QueueActionText));
        OnPropertyChanged(nameof(QueueSummary));
        OnPropertyChanged(nameof(QueueSummaryTitle));
        OnPropertyChanged(nameof(IsSigningIn));
        OnPropertyChanged(nameof(CanContinueWithoutConfirmation));
        OnPropertyChanged(nameof(CanOpenSourceSettings));
        CheckCommand.NotifyCanExecuteChanged();
        CancelChecksCommand.NotifyCanExecuteChanged();
        SignInCommand.NotifyCanExecuteChanged();
        SignInWithMethodCommand.NotifyCanExecuteChanged();
        SwitchAccountCommand.NotifyCanExecuteChanged();
        SwitchMethodAccountCommand.NotifyCanExecuteChanged();
        CopyAuthenticationCommand.NotifyCanExecuteChanged();
        StartQueueCommand.NotifyCanExecuteChanged();
        ContinueQueueCommand.NotifyCanExecuteChanged();
        SkipCommand.NotifyCanExecuteChanged();
        FinishUnconfirmedCommand.NotifyCanExecuteChanged();
        StopQueueCommand.NotifyCanExecuteChanged();
        CancelStepCommand.NotifyCanExecuteChanged();
    }

    private static SourceAuthenticationStatus GetSummaryCategory(SourceAuthenticationRowViewModel row) =>
        row.NeedsSettings ? SourceAuthenticationStatus.SettingsRequired
            : row.NeedsSignIn ? SourceAuthenticationStatus.Rejected
            : row.IsUnchecked ? SourceAuthenticationStatus.None
            : row.Check.Status;

    private static string SummaryLabel(SourceAuthenticationStatus status) => status switch
    {
        SourceAuthenticationStatus.Valid => "Подтверждено",
        SourceAuthenticationStatus.Rejected => "Нужен вход",
        SourceAuthenticationStatus.CheckFailed => "Ошибка проверки",
        SourceAuthenticationStatus.SettingsRequired => "Нужны настройки",
        SourceAuthenticationStatus.Saved => "Не подтверждено",
        SourceAuthenticationStatus.Missing => "Нет данных входа",
        _ => "Ждут проверки",
    };


}

public sealed partial class AuthenticationSummaryChip(SourceAuthenticationStatus status, string label) : ObservableObject
{
    public SourceAuthenticationStatus Status { get; } = status;
    public string Text => $"{label} {Count}";
    public bool IsVisible => Count > 0 || IsSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private int _count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _isSelected;
}
