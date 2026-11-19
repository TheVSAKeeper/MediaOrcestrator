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
    public bool CanOpenSourceSettings => _signingIn is null && _editing.Count == 0;
    public bool IsSigningIn => _signingIn is not null;
    public string? StandaloneProgressText => HasQueue ? null : ProgressText;
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

    internal void FocusSource(SourceOperationAuthenticationException failure)
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
            var methods = row.Check.Methods?.Select(method => method.Id == failure.MethodId
                ? method with { Status = SourceAuthenticationStatus.Rejected } : method).ToArray();
            CancelCheck(failure.SourceId);
            SetResult(row, new(SourceAuthenticationStatus.Rejected, Detail: "Площадка отклонила вход во время операции.", Methods: methods),
                failure.ObservedAt);
        }
        SelectedRow = row;
        SetExpanded(row);
        NotifyResults();
    }

    [RelayCommand(CanExecute = nameof(CanSignIn), IncludeCancelCommand = true)]
    private Task SignInAsync(SourceAuthenticationRowViewModel? row, CancellationToken cancellationToken) =>
        row is null ? Task.CompletedTask : SignInCoreAsync(row, row.DefaultMethodId, null, cancellationToken,
            replaceAccount: row.CanSwitchAccount);

    [RelayCommand(CanExecute = nameof(CanSignInWithMethod), IncludeCancelCommand = true)]
    private Task SignInWithMethodAsync(AuthenticationMethodRow? method, CancellationToken cancellationToken) =>
        method is null ? Task.CompletedTask : SignInCoreAsync(method.Row, method.Method.Id, null, cancellationToken, method.ChangesAccount);

    [RelayCommand]
    private static void DismissNotice(SourceAuthenticationRowViewModel? row)
    {
        if (row is not null)
        {
            row.SignInNotice = null;
        }
    }

    private async Task SignInCoreAsync(SourceAuthenticationRowViewModel row, string? methodId, QueueStep? step,
        CancellationToken cancellationToken, bool replaceAccount = false)
    {
        if (_signingIn is not null || !_rows.Contains(row))
        {
            return;
        }

        BeginInteraction(row);
        var saved = false;
        try
        {
            await WaitForCheckAsync(row, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_rows.Contains(row) || (step is not null && CurrentStep != step))
            {
                return;
            }

            var source = row.Source;
            var local = authentication.Inspect(source);
            if (local.Check.Status == SourceAuthenticationStatus.SettingsRequired
                || methodId is not null && local.Check.Methods?.Any(method => method.Id == methodId && !method.CanSignIn) == true)
            {
                row.Refresh(row.Source, row.State with { Local = local.Check, LastCheck = null });
                row.SignInError = local.Check.Detail;
                return;
            }

            string? notice = null;
            var context = new SourceAuthenticationPromptContext(row.Title, row.Platform, row.AccountFor(methodId), replaceAccount);
            await authentication.SaveSignInAsync(source, createAuthUi(context, cancellationToken, message => notice = message),
                methodId, cancellationToken);
            saved = true;
            if (!_rows.Contains(row))
            {
                return;
            }

            row.Refresh(source, authentication.ReadState(source));
            row.SignInNotice = notice;
            row.SetAwaiting(methodId, true);
            if (step is not null)
            {
                step.Saved = true;
                if (CurrentStep != step)
                {
                    step.Outcome = "данные сохранены, вход не подтверждён";
                    return;
                }
            }

            NotifyResults();
            await VerifySignInAsync(row, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            row.SignInError = saved ? "Данные сохранены. Проверку можно повторить." : "Вход отменён.";
        }
        catch (AuthenticationPromptUnavailableException ex)
        {
            row.SignInError = ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Вход в источник {SourceId} завершился ошибкой {ErrorType}", row.Source.Id, ex.GetType().Name);
            row.SignInError = "Не удалось завершить вход. Проверьте настройки источника и попробуйте снова.";
        }
        finally
        {
            EndInteraction();
        }
    }

    private async Task VerifySignInAsync(SourceAuthenticationRowViewModel row, CancellationToken token)
    {
        ProgressText = $"Проверка сохранённой сессии · {row.Title} ({row.Platform})";
        row.IsChecking = true;
        try
        {
            await CheckRowAsync(row, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            row.SignInError = "Данные сохранены. Проверку можно повторить.";
        }
        finally
        {
            row.IsChecking = false;
            NotifyResults();
        }
    }

    private void BeginInteraction(SourceAuthenticationRowViewModel row)
    {
        _signingIn = row;
        SelectedRow = row;
        SetExpanded(HasQueue ? null : row);
        row.SignInError = null;
        row.SignInNotice = null;
        ProgressText = $"Вход · {row.Title} ({row.Platform})";
        NotifyResults();
    }

    private void EndInteraction()
    {
        if (_signingIn is { SignInError: not null } row && row == QueueCurrent)
        {
            SetExpanded(row);
        }

        var rowWasReplaced = _signingIn is not null && !_rows.Contains(_signingIn);
        _signingIn = null;
        ProgressText = null;
        NotifyResults();
        if (rowWasReplaced)
        {
            ScheduleChecks();
        }
    }

    private bool CanSignIn(SourceAuthenticationRowViewModel? row) =>
        row is not null && _rows.Contains(row) && row.CanSignIn && _signingIn is null && !HasQueue && _editing.Count == 0;
    private bool CanSignInWithMethod(AuthenticationMethodRow? method) =>
        method is { CanSignIn: true } && !method.Row.IsAwaiting(method.Method.Id) && CanSignIn(method.Row);
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
    partial void OnProgressTextChanged(string? value) => OnPropertyChanged(nameof(StandaloneProgressText));

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
        OnPropertyChanged(nameof(StandaloneProgressText));
        OnPropertyChanged(nameof(CanContinueWithoutConfirmation));
        OnPropertyChanged(nameof(CanOpenSourceSettings));
        CheckCommand.NotifyCanExecuteChanged();
        CancelChecksCommand.NotifyCanExecuteChanged();
        SignInCommand.NotifyCanExecuteChanged();
        SignInWithMethodCommand.NotifyCanExecuteChanged();
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
