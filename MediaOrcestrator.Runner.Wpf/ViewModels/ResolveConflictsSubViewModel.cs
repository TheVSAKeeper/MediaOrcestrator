using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Merging;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

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
    private List<ConflictGroupViewModel>? _automationStash;

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

    internal void SetEmptyForAutomation(bool empty)
    {
        if (empty)
        {
            _automationStash ??= [.. Groups];
            Groups.ReplaceAll([]);
        }
        else if (_automationStash is not null)
        {
            Groups.ReplaceAll(_automationStash);
            _automationStash = null;
        }

        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(IsResultEmpty));
        RecountReady();
    }
}
