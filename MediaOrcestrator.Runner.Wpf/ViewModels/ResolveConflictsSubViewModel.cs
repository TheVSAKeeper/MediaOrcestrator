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

public sealed partial class ResolveConflictsSubViewModel : ObservableObject
{
    private const string StopOnErrorKey = "wpf.operations.conflicts.stop_on_error";
    private const int JournalMaxEntries = 500;

    private readonly Orcestrator _orcestrator;
    private readonly MediaMergeService _mergeService;
    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly ILogger<ResolveConflictsSubViewModel> _logger;

    private bool _suppressEvents;
    private bool _suppressPersist;
    private CancellationTokenSource? _applyCts;
    private bool _scopeLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private int _readyGroups;

    [ObservableProperty]
    private int _mediasToRemove;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(FindConflictsCommand))]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(FindConflictsCommand))]
    private bool _isApplying;

    [ObservableProperty]
    private bool _stopOnError = true;

    [ObservableProperty]
    private string _statusText = "Готов";

    [ObservableProperty]
    private bool _isJournalOpen;

    [ObservableProperty]
    private int _progressMax = 1;

    [ObservableProperty]
    private int _progressValue;

    public ResolveConflictsSubViewModel(
        Orcestrator orcestrator,
        MediaMergeService mergeService,
        ActionHolder actionHolder,
        IDialogService dialogs,
        ISettingsStore settings,
        ILogger<ResolveConflictsSubViewModel> logger)
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

    public RangeObservableCollection<ConflictGroupViewModel> Groups { get; } = [];

    public RangeObservableCollection<OperationJournalEntry> Journal { get; } = [];

    public bool HasGroups => Groups.Count > 0;

    public bool IsResultEmpty => Groups.Count == 0;

    public bool CanEditParameters => !IsApplying && !IsSearching;

    public string ApplyButtonText => ReadyGroups > 0
        ? $"Объединить ({ReadyGroups})"
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

    [RelayCommand(CanExecute = nameof(CanFindConflicts))]
    private async Task FindConflictsAsync()
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
        StatusText = "Поиск конфликтов...";

        DetachGroups();
        Groups.ReplaceAll([]);
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(IsResultEmpty));
        AppendJournal(OperationJournalLevel.Info, "── Поиск конфликтов ──");

        try
        {
            var allMedia = await Task.Run(() => _orcestrator.GetMedias());
            var allSources = await Task.Run(() => _orcestrator.GetSources());
            var groups = await Task.Run(() => MergeCandidateFinder
                .FindGroups(allMedia, scope)
                .Where(g => g.HasSourceCollision)
                .ToList());

            var newGroups = groups.Select(g => new ConflictGroupViewModel(g, allSources)).ToList();

            foreach (var group in newGroups)
            {
                group.PropertyChanged += OnGroupChanged;
            }

            Groups.ReplaceAll(newGroups);
            OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(IsResultEmpty));
            RecountReady();

            StatusText = groups.Count == 0
                ? "Конфликтов не найдено"
                : $"Найдено конфликтных групп: {groups.Count}";

            AppendJournal(OperationJournalLevel.Info, $"── Найдено конфликтных групп: {groups.Count} ──");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Поиск конфликтов сорвался");
            StatusText = "Ошибка: " + ex.Message;
            AppendJournal(OperationJournalLevel.Error, "Ошибка: " + ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanFindConflicts() => !IsSearching && !IsApplying;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var selected = Groups.Where(g => g is { IsMergeChecked: true, IsResolved: true, IsApplied: false }).ToList();

        if (selected.Count == 0)
        {
            return;
        }

        var totalRemoved = selected.Sum(g => Math.Max(g.ActiveRows.Count - 1, 0));
        var confirmed = _dialogs.ConfirmWarning(
            $"Объединить {selected.Count} групп?",
            $"Будет удалено {totalRemoved} медиа. Конфликты источников разрешаются вашим выбором. Это действие нельзя отменить.");

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
            ? $"Разбор конфликта: «{Truncate(selected[0].Title, 50)}»"
            : $"Разбор конфликтов ({selected.Count} групп)";

        var running = _actionHolder.Register(actionName, "Применение", selected.Count, _applyCts, kind: ActionKind.Metadata);
        using var scope = _actionHolder.BeginScope(running);

        var success = 0;
        var failed = 0;

        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var group = selected[i];

                running.Status = $"{i + 1}/{selected.Count}: {Truncate(group.Title, 50)}";
                StatusText = running.Status;

                try
                {
                    await Task.Run(() => ApplyGroup(group), token);

                    group.IsApplied = true;
                    group.IsApplyError = false;
                    group.ResultText = "✓ Объединено";
                    AppendJournal(OperationJournalLevel.Success,
                        $"✓ «{Truncate(group.Title, 50)}» – объединено медиа: {group.ActiveRows.Count}");
                    success++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    group.IsApplied = true;
                    group.IsApplyError = true;
                    group.ResultText = "✗ " + ex.Message;
                    AppendJournal(OperationJournalLevel.Error,
                        $"✗ «{Truncate(group.Title, 50)}» – {ex.Message}");
                    failed++;

                    _logger.LogError(ex, "Не удалось разобрать конфликт группы '{Key}'", group.Title);

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
            _logger.LogError(ex, "Пакетный разбор конфликтов сорвался");
            StatusText = "Ошибка: " + ex.Message;
            AppendJournal(OperationJournalLevel.Error, "Ошибка: " + ex.Message);
            running.Fail(ex.Message, ex);
        }
        finally
        {
            IsApplying = false;
            ProgressValue = ProgressMax;
            RecountReady();
        }
    }

    private void ApplyGroup(ConflictGroupViewModel group)
    {
        var remaining = group.ActiveRows.Select(r => r.Media).ToList();
        var target = group.TargetRow?.Media
            ?? throw new InvalidOperationException("Не выбрано целевое медиа");

        var winners = group.Conflicts
            .Where(c => c.SelectedChoice != null)
            .ToDictionary(c => c.SourceId, c => c.SelectedChoice!.ExternalId, StringComparer.Ordinal);

        var preview = _mergeService.BuildPreview(remaining, target, winners);

        _mergeService.Apply(preview, acceptConflicts: true);
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

    private bool CanApply() => !IsApplying && !IsSearching && ReadyGroups > 0;

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

    partial void OnReadyGroupsChanged(int value)
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
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        if (e.PropertyName is nameof(ConflictGroupViewModel.IsMergeChecked)
            or nameof(ConflictGroupViewModel.IsResolved)
            or nameof(ConflictGroupViewModel.IsApplied))
        {
            RecountReady();
        }
    }

    private void RecountReady()
    {
        var ready = Groups.Where(g => g is { IsMergeChecked: true, IsResolved: true, IsApplied: false }).ToList();
        ReadyGroups = ready.Count;
        MediasToRemove = ready.Sum(g => Math.Max(g.ActiveRows.Count - 1, 0));
    }

    private void DetachGroups()
    {
        foreach (var group in Groups)
        {
            group.PropertyChanged -= OnGroupChanged;
        }
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
            foreach (var group in Groups)
            {
                if (!group.IsApplied)
                {
                    group.IsMergeChecked = value;
                }
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        RecountReady();
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
}
