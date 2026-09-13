using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Merging;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MergeDuplicatesSubViewModel : ObservableObject
{
    private const string StopOnErrorKey = "wpf.operations.merge.stop_on_error";
    private const string CompactKey = "wpf.operations.merge.compact";
    private const int JournalMaxEntries = 500;

    private readonly Orcestrator _orcestrator;
    private readonly MediaMergeService _mergeService;
    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly ILogger<MergeDuplicatesSubViewModel> _logger;

    private bool _suppressEvents;
    private bool _suppressPersist;
    private CancellationTokenSource? _applyCts;
    private bool _scopeLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private int _checkedGroups;

    [ObservableProperty]
    private int _mediasToRemove;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(FindCandidatesCommand))]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(FindCandidatesCommand))]
    private bool _isApplying;

    [ObservableProperty]
    private bool _stopOnError = true;

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

    public MergeDuplicatesSubViewModel(
        Orcestrator orcestrator,
        MediaMergeService mergeService,
        ActionHolder actionHolder,
        IDialogService dialogs,
        ISettingsStore settings,
        ILogger<MergeDuplicatesSubViewModel> logger)
    {
        _orcestrator = orcestrator;
        _mergeService = mergeService;
        _actionHolder = actionHolder;
        _dialogs = dialogs;
        _settings = settings;
        _logger = logger;

        LoadPreferences();
    }

    public RangeObservableCollection<SourceCheckOptionViewModel> Scope { get; } = [];

    public RangeObservableCollection<MergeGroupRowViewModel> Groups { get; } = [];

    public RangeObservableCollection<OperationJournalEntry> Journal { get; } = [];

    public bool CanEditParameters => !IsApplying && !IsSearching;

    public bool IsResultEmpty => Groups.Count == 0;

    public string ApplyButtonText => CheckedGroups > 0
        ? $"Объединить ({CheckedGroups})"
        : "Объединить";

    public void EnsureScopeLoaded()
    {
        if (_scopeLoaded)
        {
            return;
        }

        RefreshScope();
    }

    [RelayCommand]
    private void RefreshScope()
    {
        _suppressEvents = true;
        try
        {
            var previousChecks = Scope.ToDictionary(s => s.SourceId, s => s.IsChecked, StringComparer.Ordinal);

            foreach (var option in Scope)
            {
                option.PropertyChanged -= OnScopePropertyChanged;
            }

            var newScope = _orcestrator.GetSources()
                .Where(s => !s.IsDisable)
                .OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
                .Select(source => new SourceCheckOptionViewModel
                {
                    SourceId = source.Id,
                    Title = source.TitleFull,
                    IsChecked = !previousChecks.TryGetValue(source.Id, out var was) || was,
                })
                .ToList();

            foreach (var option in newScope)
            {
                option.PropertyChanged += OnScopePropertyChanged;
            }

            Scope.ReplaceAll(newScope);
        }
        finally
        {
            _suppressEvents = false;
        }

        _scopeLoaded = true;
    }

    [RelayCommand]
    private void SelectAllScope() => SetAllScope(true);

    [RelayCommand]
    private void SelectNoScope() => SetAllScope(false);

    [RelayCommand]
    private void SelectAllGroups() => SetAllGroupChecks(true);

    [RelayCommand]
    private void SelectNoGroups() => SetAllGroupChecks(false);

    [RelayCommand]
    private void ToggleJournal() => IsJournalOpen = !IsJournalOpen;

    [RelayCommand]
    private void ClearJournal() => Journal.Clear();

    [RelayCommand(CanExecute = nameof(CanFindCandidates))]
    private async Task FindCandidatesAsync()
    {
        var scope = Scope
            .Where(s => s.IsChecked)
            .Select(s => s.SourceId)
            .ToHashSet(StringComparer.Ordinal);

        if (scope.Count == 0)
        {
            _dialogs.Info("Пустая область поиска", "Отметьте хотя бы один источник.");
            return;
        }

        IsSearching = true;
        StatusText = "Поиск кандидатов...";

        foreach (var row in Groups)
        {
            row.PropertyChanged -= OnGroupPropertyChanged;
        }

        Groups.ReplaceAll([]);
        OnPropertyChanged(nameof(IsResultEmpty));
        AppendJournal(OperationJournalLevel.Info, "── Поиск кандидатов ──");

        try
        {
            var allMedia = await Task.Run(() => _orcestrator.GetMedias());
            var allSources = await Task.Run(() => _orcestrator.GetSources());
            var allGroups = await Task.Run(() => MergeCandidateFinder.FindGroups(allMedia, scope));

            var groups = allGroups.Where(g => !g.HasSourceCollision).ToList();
            var conflictCount = allGroups.Count - groups.Count;

            var newRows = groups.Select(group =>
            {
                var members = string.Join(" ⋅ ",
                    group.Medias
                        .Where(m => !ReferenceEquals(m, group.SuggestedTarget))
                        .Select(m => FormatMedia(m, allSources)));

                var totalSources = group.Medias
                    .SelectMany(m => m.Sources ?? [])
                    .Select(s => s.SourceId)
                    .Distinct()
                    .Count();

                return new MergeGroupRowViewModel
                {
                    Group = group,
                    TargetTitle = FormatMedia(group.SuggestedTarget, allSources),
                    MergingMembers = members,
                    TotalSourcesCount = totalSources,
                };
            }).ToList();

            foreach (var row in newRows)
            {
                row.PropertyChanged += OnGroupPropertyChanged;
            }

            Groups.ReplaceAll(newRows);
            OnPropertyChanged(nameof(IsResultEmpty));
            RecountChecked();

            StatusText = groups.Count == 0
                ? conflictCount > 0
                    ? $"Чистых групп нет, но конфликтных: {conflictCount} – на вкладке «Разбор конфликтов»"
                    : "Кандидатов для объединения не найдено"
                : conflictCount > 0
                    ? $"Найдено групп: {groups.Count} · конфликтных: {conflictCount} → «Разбор конфликтов»"
                    : $"Найдено групп: {groups.Count}";

            AppendJournal(OperationJournalLevel.Info, $"── Найдено чистых групп: {groups.Count} ──");

            if (conflictCount > 0)
            {
                AppendJournal(OperationJournalLevel.Warning,
                    $"⚠ Групп с конфликтом источника: {conflictCount} – автоматически не объединяются. " +
                    "Разберите их вручную на вкладке «Разбор конфликтов».");
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Поиск кандидатов слияния сорвался");
            StatusText = "Ошибка: " + ex.Message;
            AppendJournal(OperationJournalLevel.Error, "Ошибка: " + ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanFindCandidates() => !IsSearching && !IsApplying;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var selected = Groups.Where(g => g.IsMergeChecked && !g.IsApplied).ToList();

        if (selected.Count == 0)
        {
            return;
        }

        var totalRemoved = selected.Sum(g => g.ToBeRemoved);
        var confirmed = _dialogs.ConfirmWarning(
            $"Объединить {selected.Count} групп?",
            $"Будет удалено {totalRemoved} медиа. Это действие нельзя отменить.");

        if (!confirmed)
        {
            return;
        }

        _applyCts?.Dispose();
        _applyCts = new();
        var token = _applyCts.Token;

        IsApplying = true;
        ProgressMax = Math.Max(selected.Count, 1);
        ProgressValue = 0;
        StatusText = "Применение слияний...";

        AppendJournal(OperationJournalLevel.Info,
            $"── Запуск: групп {selected.Count}, к удалению {totalRemoved} ──");

        var actionName = selected.Count == 1
            ? $"Слияние: «{selected[0].TargetTitle}»"
            : $"Слияние ({selected.Count} групп)";

        var running = _actionHolder.Register(actionName, "Применение", selected.Count, _applyCts, kind: ActionKind.Metadata);
        using var scope = _actionHolder.BeginScope(running);

        var success = 0;
        var failed = 0;

        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var row = selected[i];

                running.Status = $"{i + 1}/{selected.Count}: {Truncate(row.TargetTitle, 50)}";
                StatusText = running.Status;

                try
                {
                    await Task.Run(() =>
                    {
                        var preview = _mergeService.BuildPreview(row.Group.Medias, row.Group.SuggestedTarget);

                        _mergeService.Apply(preview, acceptConflicts: false);
                    }, token);

                    row.IsApplied = true;
                    row.IsApplyError = false;
                    row.ResultText = "Объединено";
                    AppendJournal(OperationJournalLevel.Success,
                        $"✓ «{Truncate(row.TargetTitle, 50)}» – объединено медиа: {row.MediaCount}");
                    success++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    row.IsApplied = true;
                    row.IsApplyError = true;
                    row.ResultText = ex.Message;
                    row.ResultTooltip = ex.Message;
                    AppendJournal(OperationJournalLevel.Error,
                        $"✗ «{Truncate(row.TargetTitle, 50)}» – {ex.Message}");
                    failed++;

                    _logger.LogError(ex, "Не удалось объединить группу '{Key}'", row.NormalizedKey);

                    if (StopOnError)
                    {
                        break;
                    }
                }

                ProgressValue = i + 1;
                running.SetProgress(i + 1);
            }

            StatusText = failed == 0
                ? $"Готово: объединено {success} из {selected.Count}"
                : $"Готово: {success} успешно, {failed} с ошибками";

            AppendJournal(failed == 0 ? OperationJournalLevel.Success : OperationJournalLevel.Error,
                $"── Готово: успешно {success}, с ошибками {failed} ──");

            if (failed == 0)
            {
                running.Finish($"Объединено {success} из {selected.Count}");
            }
            else
            {
                running.Fail($"Объединено {success}, ошибок {failed}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Отменено пользователем";
            AppendJournal(OperationJournalLevel.Warning, "── Прервано пользователем ──");
            running.MarkCancelled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Пакетное слияние сорвалось");
            StatusText = "Ошибка: " + ex.Message;
            AppendJournal(OperationJournalLevel.Error, "Ошибка: " + ex.Message);
            running.Fail(ex.Message, ex);
        }
        finally
        {
            IsApplying = false;
            ProgressValue = ProgressMax;
            RecountChecked();
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

    private bool CanApply() => !IsApplying && !IsSearching && CheckedGroups > 0;

    partial void OnIsApplyingChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditParameters));
    }

    partial void OnIsSearchingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditParameters));
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

    partial void OnCheckedGroupsChanged(int value)
    {
        OnPropertyChanged(nameof(ApplyButtonText));
    }

    private void LoadPreferences()
    {
        _suppressPersist = true;
        try
        {
            if (bool.TryParse(_settings.GetStringValue(StopOnErrorKey), out var stop))
            {
                StopOnError = stop;
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

    private void OnScopePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
    }

    private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressEvents || e.PropertyName != nameof(MergeGroupRowViewModel.IsMergeChecked))
        {
            return;
        }

        RecountChecked();
    }

    private void RecountChecked()
    {
        var checkedRows = Groups.Where(g => g.IsMergeChecked && !g.IsApplied).ToList();
        CheckedGroups = checkedRows.Count;
        MediasToRemove = checkedRows.Sum(g => g.ToBeRemoved);
    }

    private void SetAllScope(bool value)
    {
        _suppressEvents = true;
        try
        {
            foreach (var option in Scope)
            {
                option.IsChecked = value;
            }
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void SetAllGroupChecks(bool value)
    {
        _suppressEvents = true;
        try
        {
            foreach (var row in Groups)
            {
                if (!row.IsApplied)
                {
                    row.IsMergeChecked = value;
                }
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        RecountChecked();
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

    private static string FormatMedia(Media media, List<Source> allSources)
    {
        var sourceNames = (media.Sources ?? [])
            .Select(s => allSources.FirstOrDefault(x => x.Id == s.SourceId)?.Title ?? s.SourceId);

        return $"{media.Title} [{string.Join(", ", sourceNames)}]";
    }
}
