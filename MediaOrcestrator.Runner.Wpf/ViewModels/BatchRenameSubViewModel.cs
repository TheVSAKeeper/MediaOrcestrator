using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class BatchRenameSubViewModel : ObservableObject
{
    private const string IgnoreCaseKey = "wpf.operations.rename.ignore_case";
    private const string ModeKey = "wpf.operations.rename.mode";
    private const string StopOnErrorKey = "wpf.operations.rename.stop_on_error";
    private const string CompactKey = "wpf.operations.rename.compact";
    private const int PreviewDebounceMs = 250;
    private const int JournalMaxEntries = 500;

    private readonly Orcestrator _orcestrator;
    private readonly BatchRenameService _service;
    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly ILogger<BatchRenameSubViewModel> _logger;

    private readonly DispatcherTimer _previewTimer;
    private readonly List<Media> _medias = [];

    private bool _suppressEvents;
    private bool _suppressPersist;
    private CancellationTokenSource? _applyCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedias))]
    [NotifyPropertyChangedFor(nameof(IsSetEmpty))]
    [NotifyPropertyChangedFor(nameof(MediasSummary))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private int _mediasCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private int _actionableCount;

    [ObservableProperty]
    private string _find = string.Empty;

    [ObservableProperty]
    private string _replace = string.Empty;

    [ObservableProperty]
    private BatchRenameMode _mode = BatchRenameMode.Plain;

    [ObservableProperty]
    private bool _ignoreCase;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isApplying;

    [ObservableProperty]
    private bool _stopOnError = true;

    [ObservableProperty]
    private string? _regexError;

    [ObservableProperty]
    private string _statusText = "Готов";

    [ObservableProperty]
    private bool _isJournalOpen;

    [ObservableProperty]
    private bool _isCompact;

    [ObservableProperty]
    private int _progressMax = 1;

    [ObservableProperty]
    private int _progressValue;

    public BatchRenameSubViewModel(
        Orcestrator orcestrator,
        BatchRenameService service,
        ActionHolder actionHolder,
        IDialogService dialogs,
        ISettingsStore settings,
        ILogger<BatchRenameSubViewModel> logger)
    {
        _orcestrator = orcestrator;
        _service = service;
        _actionHolder = actionHolder;
        _dialogs = dialogs;
        _settings = settings;
        _logger = logger;

        _previewTimer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(PreviewDebounceMs),
        };
        _previewTimer.Tick += OnPreviewTimerTick;

        LoadPreferences();
    }

    public IReadOnlyList<BatchRenameModeOption> ModeOptions { get; } =
    [
        new(BatchRenameMode.Plain, "Обычная замена"),
        new(BatchRenameMode.Regex, "Регулярное выражение"),
    ];

    public RangeObservableCollection<SourceCheckOptionViewModel> Sources { get; } = [];

    public RangeObservableCollection<BatchRenameRowViewModel> Rows { get; } = [];

    public RangeObservableCollection<OperationJournalEntry> Journal { get; } = [];

    public bool HasMedias => _medias.Count > 0;

    public bool IsSetEmpty => !HasMedias;

    public string MediasSummary => _medias.Count switch
    {
        0 => "Набор пуст",
        1 => $"Набор: 1 видео",
        _ => $"Набор: {_medias.Count} видео",
    };

    public string ApplyButtonText => ActionableCount > 0
        ? $"Применить ({ActionableCount})"
        : "Применить";

    public void LoadMedia(IReadOnlyList<Media> medias, bool append = false)
    {
        if (!append)
        {
            _medias.Clear();
        }

        var seen = new HashSet<string>(_medias.Select(m => m.Id), StringComparer.Ordinal);

        foreach (var media in medias)
        {
            if (seen.Add(media.Id))
            {
                _medias.Add(media);
            }
        }

        MediasCount = _medias.Count;

        RebuildSources();
        RebuildRows();

        SchedulePreview();
    }

    [RelayCommand]
    private async Task LoadAllMediaAsync()
    {
        var all = await Task.Run(() => _orcestrator.GetMedias());
        LoadMedia(all, append: false);
        _logger.LogInformation("Переименование: загружено всё хранилище – {Count} медиа", all.Count);
    }

    [RelayCommand]
    private void ClearMedia()
    {
        _medias.Clear();
        MediasCount = 0;

        foreach (var option in Sources)
        {
            option.PropertyChanged -= OnSourceOptionPropertyChanged;
        }

        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        Sources.ReplaceAll([]);
        Rows.ReplaceAll([]);
        RegexError = null;
        RecountActionable();
    }

    [RelayCommand]
    private void SelectAllSources() => SetAllSources(true);

    [RelayCommand]
    private void SelectNoSources() => SetAllSources(false);

    [RelayCommand]
    private void SelectAllRows() => SetAllRowChecks(true);

    [RelayCommand]
    private void SelectNoRows() => SetAllRowChecks(false);

    [RelayCommand]
    private void ToggleJournal() => IsJournalOpen = !IsJournalOpen;

    [RelayCommand]
    private void ClearJournal() => Journal.Clear();

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var requests = BuildRequests();

        if (requests.Count == 0)
        {
            return;
        }

        var actionName = _medias.Count == 1
            ? $"Переименование: «{Truncate(_medias[0].Title, 50)}»"
            : $"Переименование ({requests.Count})";

        _applyCts?.Dispose();
        _applyCts = new();
        var token = _applyCts.Token;

        IsApplying = true;
        ProgressMax = Math.Max(requests.Count, 1);
        ProgressValue = 0;
        StatusText = "Подготовка";
        AppendJournal(OperationJournalLevel.Info, $"── Запуск: записей {requests.Count} ──");

        var running = _actionHolder.Register(actionName, "Подготовка", requests.Count, _applyCts, kind: ActionKind.Metadata);
        using var scope = _actionHolder.BeginScope(running);

        try
        {
            running.Status = "Авторизация площадок";

            if (!await EnsureAuthenticatedAsync(token))
            {
                StatusText = "Отменено – нет авторизации";
                AppendJournal(OperationJournalLevel.Warning, "Применение отменено: площадки не авторизованы");
                running.MarkCancelled("Нет авторизации площадок");
                return;
            }

            running.Status = $"0/{requests.Count}";

            var progress = new Progress<BatchRenameProgress>(p => OnProgress(p, running));
            var execOptions = new BatchRenameExecutionOptions(StopOnError);

            var uiContext = SynchronizationContext.Current ?? new();

            var results = await Task.Run(() => _service.ApplyAsync(requests,
                    progress,
                    result => uiContext.Post(_ => OnResultReported(result), null),
                    token,
                    execOptions),
                token);

            var success = results.Count(r => r.Success);
            var failed = results.Count - success;

            StatusText = failed == 0
                ? $"Готово: применено {success} из {results.Count}"
                : $"Готово: {success} успешно, {failed} с ошибками";

            AppendJournal(failed == 0 ? OperationJournalLevel.Success : OperationJournalLevel.Error,
                $"── Готово: успешно {success}, с ошибками {failed} ──");

            if (failed == 0)
            {
                running.Finish($"Применено {success} из {results.Count}");
            }
            else
            {
                running.Fail($"Применено {success}, ошибок {failed}");
            }

            SchedulePreview();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Отменено пользователем";
            AppendJournal(OperationJournalLevel.Warning, "── Прервано пользователем ──");
            running.MarkCancelled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Пакетное переименование сорвалось");
            StatusText = "Ошибка: " + ex.Message;
            AppendJournal(OperationJournalLevel.Error, "Ошибка: " + ex.Message);
            running.Fail(ex.Message, ex);
        }
        finally
        {
            IsApplying = false;
            ProgressValue = ProgressMax;
        }
    }

    [RelayCommand(CanExecute = nameof(IsApplyingValue))]
    private void Cancel()
    {
        try
        {
            _applyCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool IsApplyingValue() => IsApplying;

    partial void OnIsApplyingChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditParameters));
    }

    public bool CanEditParameters => !IsApplying;

    partial void OnFindChanged(string value) => SchedulePreview();

    partial void OnReplaceChanged(string value) => SchedulePreview();

    partial void OnModeChanged(BatchRenameMode value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(ModeKey, value.ToString());
        }

        SchedulePreview();
    }

    partial void OnIgnoreCaseChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(IgnoreCaseKey, value ? "true" : "false");
        }

        SchedulePreview();
    }

    partial void OnStopOnErrorChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(StopOnErrorKey, value ? "true" : "false");
        }
    }

    partial void OnIsCompactChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(CompactKey, value ? "true" : "false");
        }
    }

    partial void OnActionableCountChanged(int value)
    {
        OnPropertyChanged(nameof(ApplyButtonText));
    }

    private void LoadPreferences()
    {
        _suppressPersist = true;
        try
        {
            if (bool.TryParse(_settings.GetStringValue(IgnoreCaseKey), out var ignoreCase))
            {
                IgnoreCase = ignoreCase;
            }

            if (Enum.TryParse<BatchRenameMode>(_settings.GetStringValue(ModeKey), out var mode))
            {
                Mode = mode == BatchRenameMode.Regex ? BatchRenameMode.Regex : BatchRenameMode.Plain;
            }

            if (bool.TryParse(_settings.GetStringValue(StopOnErrorKey), out var stopOnError))
            {
                StopOnError = stopOnError;
            }

            if (bool.TryParse(_settings.GetStringValue(CompactKey), out var compact))
            {
                IsCompact = compact;
            }
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    private void OnSourceOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressEvents || e.PropertyName != nameof(SourceCheckOptionViewModel.IsChecked))
        {
            return;
        }

        SchedulePreview();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressEvents || e.PropertyName != nameof(BatchRenameRowViewModel.IsApplyChecked))
        {
            return;
        }

        RecountActionable();
    }

    private void RebuildSources()
    {
        _suppressEvents = true;
        try
        {
            var previousChecks = Sources.ToDictionary(s => s.SourceId, s => s.IsChecked, StringComparer.Ordinal);

            foreach (var option in Sources)
            {
                option.PropertyChanged -= OnSourceOptionPropertyChanged;
            }

            var referenced = _service.GetReferencedSources(_medias);
            var newSources = referenced
                .Select(info => new SourceCheckOptionViewModel
                {
                    SourceId = info.SourceId,
                    Title = info.Title,
                    IsChecked = !previousChecks.TryGetValue(info.SourceId, out var was) || was,
                })
                .ToList();

            foreach (var option in newSources)
            {
                option.PropertyChanged += OnSourceOptionPropertyChanged;
            }

            Sources.ReplaceAll(newSources);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void RebuildRows()
    {
        _suppressEvents = true;
        try
        {
            foreach (var row in Rows)
            {
                row.PropertyChanged -= OnRowPropertyChanged;
            }

            var newRows = _medias
                .Select(media =>
                {
                    var title = media.Title ?? string.Empty;
                    return new BatchRenameRowViewModel
                    {
                        Media = media,
                        OldTitle = title,
                        NewTitle = title,
                    };
                })
                .ToList();

            foreach (var row in newRows)
            {
                row.PropertyChanged += OnRowPropertyChanged;
            }

            Rows.ReplaceAll(newRows);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void SchedulePreview()
    {
        if (IsApplying || !HasMedias)
        {
            return;
        }

        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void OnPreviewTimerTick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_medias.Count == 0)
        {
            RegexError = null;
            RecountActionable();
            return;
        }

        var options = CurrentOptions();
        var previews = _service.Preview(_medias, options);
        string? firstError = null;

        foreach (var preview in previews)
        {
            var row = Rows.FirstOrDefault(r => ReferenceEquals(r.Media, preview.Media));

            if (row is null)
            {
                continue;
            }

            row.Sources = preview.Sources;
            row.NewTitle = preview.NewTitle;
            row.PreviewError = preview.Error;

            if (preview.Error is not null)
            {
                firstError ??= preview.Error;
            }

            UpdateRowSummary(row, preview);
        }

        RegexError = firstError;
        RecountActionable();
    }

    private static void UpdateRowSummary(BatchRenameRowViewModel row, BatchRenamePreview preview)
    {
        row.SourceChips = BuildSourceChips(preview.Sources);

        var updatable = preview.Sources.Where(s => s.CanUpdate).ToList();

        if (updatable.Count == 0)
        {
            row.SourcesSummary = "–";
            row.SourcesTooltip = "Нет площадок, доступных для переименования";
            row.HasUpdatableSources = false;
            row.IsOutOfSync = false;
            return;
        }

        var drift = updatable
            .Where(s => !s.InSync)
            .Select(s => s.SourceTitle)
            .ToList();

        row.HasUpdatableSources = true;
        row.IsOutOfSync = drift.Count > 0;

        row.SourcesSummary = drift.Count > 0
            ? "✗ " + string.Join(", ", drift)
            : "✓ синхронно";

        var tooltip = new StringBuilder();

        foreach (var source in preview.Sources)
        {
            var mark = !source.CanUpdate ? "•" : source.InSync ? "✓" : "✗";
            tooltip.Append(mark).Append(' ').Append(source.SourceTitle);

            if (!source.CanUpdate)
            {
                tooltip.Append(" – ").Append(source.SkipReason);
            }
            else if (!source.InSync)
            {
                tooltip.Append(" – сейчас «").Append(Truncate(source.CurrentTitle, 50)).Append('»');
            }
            else
            {
                tooltip.Append(" – актуально");
            }

            tooltip.AppendLine();
        }

        row.SourcesTooltip = tooltip.ToString().TrimEnd();
    }

    private static IReadOnlyList<OperationSourceChipViewModel> BuildSourceChips(IReadOnlyList<BatchRenameSourcePreview> sources)
    {
        return sources
            .Select(source =>
            {
                if (!source.CanUpdate)
                {
                    return new OperationSourceChipViewModel
                    {
                        Text = source.SourceTitle,
                        State = OperationSourceChipState.Skipped,
                        Tooltip = string.IsNullOrEmpty(source.SkipReason)
                            ? "Переименование не поддерживается"
                            : source.SkipReason,
                    };
                }

                if (source.InSync)
                {
                    return new OperationSourceChipViewModel
                    {
                        Text = source.SourceTitle,
                        State = OperationSourceChipState.Synced,
                        Tooltip = "Название актуально",
                    };
                }

                return new OperationSourceChipViewModel
                {
                    Text = source.SourceTitle,
                    State = OperationSourceChipState.OutOfSync,
                    Tooltip = $"Сейчас: «{Truncate(source.CurrentTitle, 60)}»",
                };
            })
            .ToList();
    }

    private void RecountActionable()
    {
        ActionableCount = Rows.Count(r => r.IsActionable);
    }

    private bool CanApply() => !IsApplying && ActionableCount > 0 && RegexError is null;

    private BatchRenameOptions CurrentOptions()
    {
        return new(Find, Replace, Mode, IgnoreCase, BuildAllowedIds());
    }

    private IReadOnlyCollection<string>? BuildAllowedIds()
    {
        if (Sources.Count == 0)
        {
            return null;
        }

        if (Sources.All(s => s.IsChecked))
        {
            return null;
        }

        return Sources
            .Where(s => s.IsChecked)
            .Select(s => s.SourceId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private List<BatchRenameRequest> BuildRequests()
    {
        var allowed = BuildAllowedIds();
        var list = new List<BatchRenameRequest>();

        foreach (var row in Rows)
        {
            if (!row.IsActionable)
            {
                continue;
            }

            list.Add(new(row.Media, row.NewTitle, null, allowed));
        }

        return list;
    }

    private async Task<bool> EnsureAuthenticatedAsync(CancellationToken token)
    {
        var allowed = BuildAllowedIds() ?? Sources.Select(s => s.SourceId).ToHashSet(StringComparer.Ordinal);

        if (allowed.Count == 0)
        {
            return true;
        }

        var pending = _service.GetUnauthenticatedSources(allowed);

        if (pending.Count == 0)
        {
            return true;
        }

        var message = "Перед переименованием нужно войти на площадки:" + Environment.NewLine
            + string.Join(Environment.NewLine, pending.Select(p => "• " + p.Title)) + Environment.NewLine
            + Environment.NewLine
            + "В WPF-версии auth-flow ещё не подключён. Войдите через WinForms-Runner и попробуйте снова.";

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => _dialogs.Warning("Требуется авторизация", message));
        }
        else
        {
            _dialogs.Warning("Требуется авторизация", message);
        }

        token.ThrowIfCancellationRequested();
        return false;
    }

    private void OnProgress(BatchRenameProgress progress, ActionHolder.RunningAction running)
    {
        if (progress.Total > 0)
        {
            ProgressMax = progress.Total;
            ProgressValue = Math.Min(progress.Processed, progress.Total);
        }

        var current = string.IsNullOrEmpty(progress.CurrentTitle) ? "..." : Truncate(progress.CurrentTitle, 50);
        StatusText = $"Применение {Math.Min(progress.Processed + 1, progress.Total)}/{progress.Total}: {current}";

        if (running.State == ActionState.Running && progress.Total > 0)
        {
            running.ProgressMax = progress.Total;
            running.SetProgress(Math.Min(progress.Processed, progress.Total));
            running.Status = $"{Math.Min(progress.Processed, progress.Total)}/{progress.Total}";
        }
    }

    private void OnResultReported(BatchRenameResult result)
    {
        var row = Rows.FirstOrDefault(r => r.Media.Id == result.Media.Id);

        if (row is not null)
        {
            row.IsApplied = true;
            row.IsApplyError = !result.Success;
            row.ResultTooltip = BuildResultTooltip(result);
        }

        if (result.Success)
        {
            var changed = !string.Equals(result.OldTitle, result.NewTitle, StringComparison.Ordinal);
            AppendJournal(OperationJournalLevel.Success,
                changed
                    ? $"✓ «{Truncate(result.OldTitle, 50)}» → «{Truncate(result.NewTitle, 50)}»"
                    : $"✓ «{Truncate(result.NewTitle, 50)}» – площадки досинхронизированы");
        }
        else
        {
            AppendJournal(OperationJournalLevel.Error,
                $"✗ «{Truncate(result.OldTitle, 50)}» – {result.ErrorMessage ?? "ошибка"}");
        }

        foreach (var source in result.Sources)
        {
            AppendJournal(SourceOutcomeLevel(source.Outcome),
                $"{source.SourceTitle} – {OutcomeText(source)}",
                indent: 1);
        }
    }

    private void SetAllSources(bool value)
    {
        _suppressEvents = true;
        try
        {
            foreach (var source in Sources)
            {
                source.IsChecked = value;
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        SchedulePreview();
    }

    private void SetAllRowChecks(bool value)
    {
        _suppressEvents = true;
        try
        {
            foreach (var row in Rows)
            {
                row.IsApplyChecked = value;
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        RecountActionable();
    }

    private void AppendJournal(OperationJournalLevel level, string text, int indent = 0)
    {
        Journal.Add(new(DateTime.Now, level, text, indent));

        while (Journal.Count > JournalMaxEntries)
        {
            Journal.RemoveAt(0);
        }
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value ?? string.Empty;
        }

        return value.AsSpan(0, max - 1).ToString() + "…";
    }

    private static string OutcomeText(BatchRenameSourceResult source)
    {
        return source.Outcome switch
        {
            BatchRenameSourceOutcome.Updated => "обновлено",
            BatchRenameSourceOutcome.AlreadyUpToDate => "уже актуально",
            BatchRenameSourceOutcome.Skipped => $"пропуск ({source.Message})",
            BatchRenameSourceOutcome.NotSupported => "обновление не поддерживается",
            BatchRenameSourceOutcome.VerificationFailed => "название на площадке не изменилось",
            BatchRenameSourceOutcome.Failed => $"ошибка – {source.Message}",
            _ => source.Message ?? string.Empty,
        };
    }

    private static OperationJournalLevel SourceOutcomeLevel(BatchRenameSourceOutcome outcome)
    {
        return outcome switch
        {
            BatchRenameSourceOutcome.Updated => OperationJournalLevel.Success,
            BatchRenameSourceOutcome.AlreadyUpToDate => OperationJournalLevel.Info,
            BatchRenameSourceOutcome.Skipped => OperationJournalLevel.Info,
            BatchRenameSourceOutcome.NotSupported => OperationJournalLevel.Warning,
            BatchRenameSourceOutcome.VerificationFailed => OperationJournalLevel.Error,
            BatchRenameSourceOutcome.Failed => OperationJournalLevel.Error,
            _ => OperationJournalLevel.Info,
        };
    }

    private static string BuildResultTooltip(BatchRenameResult result)
    {
        var tip = new StringBuilder();
        tip.AppendLine(result.Success ? "Применено" : "Ошибка: " + (result.ErrorMessage ?? "неизвестно"));

        foreach (var source in result.Sources)
        {
            tip.Append("• ").Append(source.SourceTitle).Append(" – ").AppendLine(OutcomeText(source));
        }

        return tip.ToString().TrimEnd();
    }
}

public sealed record BatchRenameModeOption(BatchRenameMode Value, string Label);
