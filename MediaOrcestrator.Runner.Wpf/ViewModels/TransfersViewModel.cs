using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class TransfersViewModel : ObservableObject, IPageHeader, IPageRenderMetric
{
    public string PageTitle => "Передачи";

    public string? PageDescription =>
        "Перенос медиа между источниками по заданным связям. План строится автоматически, запуск – ручной.";

    string? IPageRenderMetric.RenderDurationText => RenderDurationText;

    IReadOnlyList<PageRenderStage>? IPageRenderMetric.RenderStages => RenderStages;


    private const string JournalOpenSettingsKey = "wpf.transfers.journal_open";
    private const string StopOnErrorSettingsKey = "wpf.transfers.stop_on_error";
    private const string UploadIntervalEnabledSettingsKey = "wpf.transfers.upload_interval_enabled";
    private const string UploadIntervalMinutesSettingsKey = "wpf.transfers.upload_interval_minutes";
    private const int DefaultUploadIntervalMinutes = 5;
    private const int MaxUploadIntervalMinutes = 1440;
    private const int JournalMaxEntries = 500;
    private const int SearchDebounceMs = 300;

    private readonly Orcestrator _orcestrator;
    private readonly SyncPlanner _planner;
    private readonly SyncRetryRunner _retryRunner;
    private readonly ActionHolder _actionHolder;
    private readonly ISettingsStore _settings;
    private readonly INavigationService _navigation;
    private readonly ILogger<TransfersViewModel> _logger;

    private readonly List<SyncIntent> _allRootIntents = [];
    private readonly DispatcherTimer _searchTimer;
    private List<Source> _planSources = [];
    private List<SourceSyncRelation> _planRelations = [];

    private CancellationTokenSource? _runCts;
    private bool _loaded;
    private bool _suppressPersist;
    private bool _suppressFilterApply;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    [NotifyPropertyChangedFor(nameof(PlanSummary))]
    [NotifyPropertyChangedFor(nameof(IsEmptyAfterBuild))]
    private int _planRoots;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlanSummary))]
    private int _planSteps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnySelected))]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    [NotifyPropertyChangedFor(nameof(ExecuteButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteSelectedCommand))]
    private int _selectedRoots;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    [NotifyPropertyChangedFor(nameof(ExecuteButtonText))]
    private int _selectedSteps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    private bool _isPlanLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildPlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    private bool _isRebuilding;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildPlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(HasRunProgress))]
    [NotifyPropertyChangedFor(nameof(CanEditUploadInterval))]
    [NotifyPropertyChangedFor(nameof(CanEditUploadIntervalMinutes))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRunProgress))]
    [NotifyPropertyChangedFor(nameof(RunProgressPercent))]
    [NotifyPropertyChangedFor(nameof(RunProgressText))]
    private int _runStepsTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunProgressPercent))]
    [NotifyPropertyChangedFor(nameof(RunProgressText))]
    private int _runStepsCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunProgressText))]
    private int _runStepsFailed;

    [ObservableProperty]
    private bool _isPlanStale;

    [ObservableProperty]
    private bool _stopOnError = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditUploadIntervalMinutes))]
    [NotifyPropertyChangedFor(nameof(IsUploadIntervalValid))]
    [NotifyPropertyChangedFor(nameof(UploadIntervalHint))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteSelectedCommand))]
    private bool _uploadIntervalEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UploadIntervalMinutes))]
    [NotifyPropertyChangedFor(nameof(IsUploadIntervalValid))]
    [NotifyPropertyChangedFor(nameof(UploadIntervalHint))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteSelectedCommand))]
    private string _uploadIntervalMinutesText = DefaultUploadIntervalMinutes.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    private bool _isJournalOpen;

    [ObservableProperty]
    private bool _isMoreMenuOpen;

    [ObservableProperty]
    private string _statusText = "Готов";

    [ObservableProperty]
    private string? _emptyHint;

    [ObservableProperty]
    private string _emptyHeading = "Передач нет";

    [ObservableProperty]
    private PackIconLucideKind _emptyIcon = PackIconLucideKind.CircleCheck;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearch))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _renderDurationText;

    [ObservableProperty]
    private IReadOnlyList<PageRenderStage>? _renderStages;

    public TransfersViewModel(
        Orcestrator orcestrator,
        SyncPlanner planner,
        SyncRetryRunner retryRunner,
        ActionHolder actionHolder,
        ISettingsStore settings,
        INavigationService navigation,
        ILogger<TransfersViewModel> logger)
    {
        _orcestrator = orcestrator;
        _planner = planner;
        _retryRunner = retryRunner;
        _actionHolder = actionHolder;
        _settings = settings;
        _navigation = navigation;
        _logger = logger;

        Groups = [];
        Journal = [];
        SourceFilters = [];
        RelationFilters = [];

        _searchTimer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(SearchDebounceMs),
        };
        _searchTimer.Tick += OnSearchTimerTick;

        _suppressPersist = true;
        IsJournalOpen = bool.TryParse(_settings.GetStringValue(JournalOpenSettingsKey), out var open) && open;
        StopOnError = !bool.TryParse(_settings.GetStringValue(StopOnErrorSettingsKey), out var stop) || stop;
        UploadIntervalEnabled = bool.TryParse(_settings.GetStringValue(UploadIntervalEnabledSettingsKey), out var interval) && interval;
        UploadIntervalMinutesText = (ParseUploadInterval(_settings.GetStringValue(UploadIntervalMinutesSettingsKey)) ?? DefaultUploadIntervalMinutes)
            .ToString(CultureInfo.InvariantCulture);
        _suppressPersist = false;
    }

    public RangeObservableCollection<FilterOptionViewModel> SourceFilters { get; }

    public RangeObservableCollection<FilterOptionViewModel> RelationFilters { get; }

    public bool HasSearch => !string.IsNullOrEmpty(SearchText);

    public RangeObservableCollection<TransferMediaGroupViewModel> Groups { get; }

    public RangeObservableCollection<TransferJournalEntry> Journal { get; }

    public bool HasPlan => IsPlanLoaded && PlanRoots > 0;

    public bool IsEmptyAfterBuild => IsPlanLoaded && PlanRoots == 0;

    public bool HasAnySelected => SelectedRoots > 0;

    public string PlanSummary => IsPlanLoaded
        ? $"План: {PlanRoots} цепочек · {PlanSteps} шагов"
        : "План ещё не построен";

    public string SelectionSummary => $"Выбрано: {SelectedRoots} цепочек / {SelectedSteps} шагов";

    public string ExecuteButtonText => HasAnySelected
        ? $"▶ Выполнить выбранное ({SelectedRoots} цепочек · {SelectedSteps} шагов)"
        : "▶ Выполнить выбранное";

    public bool HasRunProgress => IsRunning && RunStepsTotal > 0;

    public bool CanEditUploadInterval => !IsRunning;

    public bool CanEditUploadIntervalMinutes => UploadIntervalEnabled && !IsRunning;

    public int? UploadIntervalMinutes => ParseUploadInterval(UploadIntervalMinutesText);

    public bool IsUploadIntervalValid => !UploadIntervalEnabled || UploadIntervalMinutes is not null;

    public string UploadIntervalHint => IsUploadIntervalValid
        ? "Пауза после каждой успешной загрузки – ВК хуже показывает видео, залитые пачкой."
        : $"Интервал – целое число минут от 1 до {MaxUploadIntervalMinutes}.";

    public double RunProgressPercent => RunStepsTotal > 0
        ? Math.Min(100.0, RunStepsCompleted * 100.0 / RunStepsTotal)
        : 0.0;

    public string RunProgressText => RunStepsTotal > 0
        ? RunStepsFailed > 0
            ? $"{RunStepsCompleted}/{RunStepsTotal} шагов · ошибок: {RunStepsFailed}"
            : $"{RunStepsCompleted}/{RunStepsTotal} шагов"
        : string.Empty;

    public async Task EnsureLoadedAsync()
    {
        if (IsRebuilding || IsRunning)
        {
            return;
        }

        if (_loaded)
        {
            if (IsPlanLoaded)
            {
                IsPlanStale = true;
            }

            return;
        }

        _loaded = true;
        await RebuildPlanInternalAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRebuild))]
    private Task RebuildPlanAsync() => RebuildPlanInternalAsync();

    private bool CanRebuild() => !IsRebuilding && !IsRunning;

    private async Task RebuildPlanInternalAsync()
    {
        IsRebuilding = true;
        StatusText = "Построение плана…";

        long dbMs = 0;
        long filtersMs = 0;
        long planMs = 0;
        TransferFilterBreakdown? applyBreakdown = null;

        try
        {
            var dbSw = Stopwatch.StartNew();
            var (sources, medias, relations) = await Task.Run(() =>
                (_orcestrator.GetSources(), _orcestrator.GetMedias(), _orcestrator.GetRelations()));
            dbSw.Stop();
            dbMs = dbSw.ElapsedMilliseconds;

            _planSources = sources;
            _planRelations = relations;

            var filtersSw = Stopwatch.StartNew();
            PopulateFilters(sources, relations);
            filtersSw.Stop();
            filtersMs = filtersSw.ElapsedMilliseconds;

            if (sources.Count == 0)
            {
                applyBreakdown = ApplyPlanInternal([], 0);
                EmptyIcon = PackIconLucideKind.SlidersHorizontal;
                EmptyHeading = "Источники не настроены";
                EmptyHint = "Источники не настроены. Добавьте источник в разделе «Источники и связи», чтобы появились медиа и передачи.";
                StatusText = "Готов";
                return;
            }

            if (relations.Count == 0)
            {
                applyBreakdown = ApplyPlanInternal([], 0);
                EmptyIcon = PackIconLucideKind.Link2;
                EmptyHeading = "Связи не настроены";
                EmptyHint = "Связи не настроены. Чтобы передачи появились, добавьте связь в разделе «Источники и связи».";
                StatusText = "Готов";
                return;
            }

            var planSw = Stopwatch.StartNew();
            var rootIntents = await Task.Run(() => _planner.Plan(medias, relations));
            var totalSteps = CountSteps(rootIntents);
            planSw.Stop();
            planMs = planSw.ElapsedMilliseconds;

            applyBreakdown = ApplyPlanInternal(rootIntents, totalSteps);

            EmptyIcon = PackIconLucideKind.CircleCheck;
            EmptyHeading = "Передач нет";
            EmptyHint = rootIntents.Count == 0
                ? "Всё в порядке: медиа присутствуют во всех целевых источниках по активным связям."
                : null;

            StatusText = "Готов";
            _logger.LogInformation("План передач построен: {Roots} цепочек, {Steps} шагов", rootIntents.Count, totalSteps);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось построить план передач");
            StatusText = $"Ошибка построения плана: {ex.Message}";
            AppendJournal(TransferJournalLevel.Error, $"Ошибка построения плана: {ex.Message}");
        }
        finally
        {
            IsRebuilding = false;
        }

        if (applyBreakdown is { } b)
        {
            var vmMs = dbMs + filtersMs + planMs + b.GroupingMs + b.NodesMs + b.CommitMs;
            PublishRenderMetric(
            [
                new("Чтение БД", FormatMs(dbMs)),
                new("Подготовка фильтров", FormatMs(filtersMs)),
                new("Планирование", FormatMs(planMs)),
                new("Группировка медиа", FormatMs(b.GroupingMs)),
                new("Построение узлов дерева", FormatMs(b.NodesMs)),
                new("Подключение и пересчёт", FormatMs(b.CommitMs)),
            ], vmMs);
        }
    }

    private TransferFilterBreakdown ApplyPlanInternal(List<SyncIntent> rootIntents, int totalSteps)
    {
        _allRootIntents.Clear();
        _allRootIntents.AddRange(rootIntents);

        PlanRoots = rootIntents.Count;
        PlanSteps = totalSteps;
        IsPlanLoaded = true;
        IsPlanStale = false;

        return ApplyFilter();
    }

    private TransferFilterBreakdown ApplyFilter()
    {
        var groupingSw = Stopwatch.StartNew();

        foreach (var group in Groups)
        {
            DetachGroup(group);
        }

        if (_allRootIntents.Count == 0)
        {
            Groups.ReplaceAll([]);
            RecomputeCounts();
            groupingSw.Stop();
            return new(groupingSw.ElapsedMilliseconds, 0, 0);
        }

        var search = SearchText?.Trim() ?? string.Empty;
        var sourceFilter = SourceFilters
            .Where(f => f.IsChecked)
            .Select(f => f.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationFilter = RelationFilters
            .Where(f => f.IsChecked)
            .Select(f => f.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var groupedByMedia = _allRootIntents
            .GroupBy(i => i.Media.Id)
            .OrderBy(g => g.Min(i => i.Sort))
            .ThenBy(g => g.First().Media.Title, NaturalStringComparer.Instance)
            .ToList();
        groupingSw.Stop();

        var nodesSw = Stopwatch.StartNew();
        var newGroups = new List<TransferMediaGroupViewModel>();

        foreach (var bucket in groupedByMedia)
        {
            var media = bucket.First().Media;

            if (search.Length > 0 && media.Title.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (sourceFilter.Count > 0 && !media.Sources.Any(s => sourceFilter.Contains(s.SourceId)))
            {
                continue;
            }

            var group = new TransferMediaGroupViewModel(this, media);
            var rootNodes = new List<TransferIntentNodeViewModel>();

            foreach (var rootIntent in bucket.OrderBy(i => i.From.TitleFull, NaturalStringComparer.Instance).ThenBy(i => i.Sort))
            {
                if (relationFilter.Count > 0 && !relationFilter.Contains(RelationKey(rootIntent.Relation)))
                {
                    continue;
                }

                rootNodes.Add(BuildNode(rootIntent, parent: null, relationFilter));
            }

            if (rootNodes.Count == 0)
            {
                continue;
            }

            group.Children.AddRange(rootNodes);
            group.Recompute();
            newGroups.Add(group);
        }
        nodesSw.Stop();

        var commitSw = Stopwatch.StartNew();
        foreach (var group in newGroups)
        {
            AttachGroup(group);
        }
        Groups.ReplaceAll(newGroups);
        RecomputeCounts();
        commitSw.Stop();

        return new(groupingSw.ElapsedMilliseconds, nodesSw.ElapsedMilliseconds, commitSw.ElapsedMilliseconds);
    }

    private static string RelationKey(SourceSyncRelation relation) => $"{relation.FromId}|{relation.ToId}";

    private void PopulateFilters(List<Source> sources, List<SourceSyncRelation> relations)
    {
        _suppressFilterApply = true;

        var prevSources = SourceFilters.Where(f => f.IsChecked).Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prevRelations = RelationFilters.Where(f => f.IsChecked).Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        SourceFilters.ReplaceAll(sources
            .OrderBy(s => s.TitleFull, NaturalStringComparer.Instance)
            .Select(s => new FilterOptionViewModel(s.Id, s.TitleFull, OnFilterToggled)
            {
                IsChecked = prevSources.Contains(s.Id),
            }));

        RelationFilters.ReplaceAll(relations
            .Select(r =>
            {
                var id = RelationKey(r);
                return new FilterOptionViewModel(id, $"{r.From.TitleFull} → {r.To.TitleFull}", OnFilterToggled)
                {
                    IsChecked = prevRelations.Contains(id),
                };
            }));

        _suppressFilterApply = false;
    }

    private void OnFilterToggled(FilterOptionViewModel _)
    {
        if (_suppressFilterApply)
        {
            return;
        }

        ApplyFilterAndPublish();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OnSearchTimerTick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        ApplyFilterAndPublish();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        _searchTimer.Stop();
        ApplyFilterAndPublish();
    }

    [RelayCommand]
    private void ResetFilters()
    {
        _suppressFilterApply = true;

        foreach (var f in SourceFilters)
        {
            f.IsChecked = false;
        }

        foreach (var f in RelationFilters)
        {
            f.IsChecked = false;
        }

        SearchText = string.Empty;
        _searchTimer.Stop();
        _suppressFilterApply = false;

        ApplyFilterAndPublish();
        IsMoreMenuOpen = false;
    }

    private void ApplyFilterAndPublish()
    {
        var b = ApplyFilter();
        var vmMs = b.GroupingMs + b.NodesMs + b.CommitMs;
        PublishRenderMetric(
        [
            new("Группировка медиа", FormatMs(b.GroupingMs)),
            new("Построение узлов дерева", FormatMs(b.NodesMs)),
            new("Подключение и пересчёт", FormatMs(b.CommitMs)),
        ], vmMs);
    }

    private void PublishRenderMetric(IReadOnlyList<PageRenderStage> vmStages, long vmMs)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            ApplyMetric(vmStages, vmMs, renderMs: 0);
            return;
        }

        var renderSw = Stopwatch.StartNew();

        dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            renderSw.Stop();
            ApplyMetric(vmStages, vmMs, renderSw.ElapsedMilliseconds);
        }));
    }

    private void ApplyMetric(IReadOnlyList<PageRenderStage> vmStages, long vmMs, long renderMs)
    {
        var totalMs = vmMs + renderMs;

        var nodeCount = 0;

        foreach (var group in Groups)
        {
            foreach (var child in group.Children)
            {
                nodeCount += CountNodesRecursive(child);
            }
        }

        var stages = new List<PageRenderStage>(vmStages.Count + 5);
        stages.AddRange(vmStages);
        stages.Add(new("Отрисовка кадра", FormatMs(renderMs)));
        stages.Add(new("Итого", FormatMs(totalMs)));
        stages.Add(new("Цепочек (корней)", Groups.Sum(g => g.Children.Count).ToString()));
        stages.Add(new("Узлов всего", nodeCount.ToString()));
        stages.Add(new("Групп медиа", Groups.Count.ToString()));

        RenderDurationText = FormatMs(totalMs);
        RenderStages = stages;
    }

    private static int CountNodesRecursive(TransferIntentNodeViewModel node)
    {
        var total = 1;

        foreach (var child in node.Children)
        {
            total += CountNodesRecursive(child);
        }

        return total;
    }

    private static string FormatMs(long ms) => $"{ms} мс";

    private sealed record TransferFilterBreakdown(long GroupingMs, long NodesMs, long CommitMs);

    private TransferIntentNodeViewModel BuildNode(SyncIntent intent, TransferIntentNodeViewModel? parent, HashSet<string>? relationFilter = null)
    {
        var node = new TransferIntentNodeViewModel(this, intent, parent);
        node.PropertyChanged += OnNodePropertyChanged;

        var childNodes = new List<TransferIntentNodeViewModel>(intent.NextIntents.Count);

        foreach (var child in intent.NextIntents)
        {
            if (relationFilter is { Count: > 0 } && !relationFilter.Contains(RelationKey(child.Relation)))
            {
                continue;
            }

            childNodes.Add(BuildNode(child, node, relationFilter));
        }

        if (childNodes.Count > 0)
        {
            node.Children.AddRange(childNodes);
        }

        return node;
    }

    private void OnNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TransferIntentNodeViewModel.IsSelected))
        {
            return;
        }

        ExecuteSelectedCommand.NotifyCanExecuteChanged();
    }

    private void AttachGroup(TransferMediaGroupViewModel group)
    {
        foreach (var child in group.Children)
        {
            AttachNodeRecursive(child);
        }
    }

    private void AttachNodeRecursive(TransferIntentNodeViewModel node)
    {
        node.PropertyChanged += OnNodePropertyChanged;

        foreach (var c in node.Children)
        {
            AttachNodeRecursive(c);
        }
    }

    private void DetachGroup(TransferMediaGroupViewModel group)
    {
        foreach (var child in group.Children)
        {
            DetachNodeRecursive(child);
        }
    }

    private void DetachNodeRecursive(TransferIntentNodeViewModel node)
    {
        node.PropertyChanged -= OnNodePropertyChanged;

        foreach (var c in node.Children)
        {
            DetachNodeRecursive(c);
        }
    }

    internal void OnNodeSelectionChanged(TransferIntentNodeViewModel node, bool value)
    {
        if (value)
        {
            var ancestor = node.Parent;

            while (ancestor != null)
            {
                ancestor.SetIsSelectedSilent(true);
                ancestor = ancestor.Parent;
            }
        }
        else
        {
            foreach (var child in node.Children)
            {
                ClearDescendantsSilent(child);
            }
        }

        var group = FindGroupOf(node);
        group?.Recompute();

        RecomputeCounts();
    }

    internal void OnGroupBulkSelection()
    {
        RecomputeCounts();
    }

    private static void ClearDescendantsSilent(TransferIntentNodeViewModel node)
    {
        node.SetIsSelectedSilent(false);

        foreach (var child in node.Children)
        {
            ClearDescendantsSilent(child);
        }
    }

    private TransferMediaGroupViewModel? FindGroupOf(TransferIntentNodeViewModel node)
    {
        var root = node;

        while (root.Parent != null)
        {
            root = root.Parent;
        }

        var mediaId = root.Intent.Media.Id;

        foreach (var group in Groups)
        {
            if (string.Equals(group.Media.Id, mediaId, StringComparison.OrdinalIgnoreCase))
            {
                return group;
            }
        }

        return null;
    }

    private void RecomputeCounts()
    {
        var roots = 0;
        var steps = 0;

        foreach (var group in Groups)
        {
            foreach (var child in group.Children)
            {
                if (child.IsSelected)
                {
                    roots++;
                }

                steps += CountSelectedRecursive(child);
            }
        }

        SelectedRoots = roots;
        SelectedSteps = steps;
    }

    private static int CountSelectedRecursive(TransferIntentNodeViewModel node)
    {
        var total = node.IsSelected ? 1 : 0;

        foreach (var child in node.Children)
        {
            total += CountSelectedRecursive(child);
        }

        return total;
    }

    private static int CountSteps(List<SyncIntent> roots)
    {
        var total = 0;

        foreach (var root in roots)
        {
            total += CountStepsRecursive(root);
        }

        return total;
    }

    private static int CountStepsRecursive(SyncIntent intent)
    {
        var total = 1;

        foreach (var child in intent.NextIntents)
        {
            total += CountStepsRecursive(child);
        }

        return total;
    }

    [RelayCommand(CanExecute = nameof(CanSelectBulk))]
    private void SelectAll()
    {
        SetBulkSelection(true);
        IsMoreMenuOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanSelectBulk))]
    private void DeselectAll()
    {
        SetBulkSelection(false);
        IsMoreMenuOpen = false;
    }

    private bool CanSelectBulk() => !IsRunning && !IsRebuilding && HasPlan;

    private void SetBulkSelection(bool value)
    {
        foreach (var group in Groups)
        {
            foreach (var child in group.Children)
            {
                SetSilentRecursive(child, value);
            }

            group.Recompute();
        }

        RecomputeCounts();
    }

    private static void SetSilentRecursive(TransferIntentNodeViewModel node, bool value)
    {
        node.SetIsSelectedSilent(value);

        foreach (var child in node.Children)
        {
            SetSilentRecursive(child, value);
        }
    }

    [RelayCommand]
    private void ExpandAll()
    {
        SetExpansionAll(true);
        IsMoreMenuOpen = false;
    }

    [RelayCommand]
    private void CollapseAll()
    {
        SetExpansionAll(false);
        IsMoreMenuOpen = false;
    }

    private void SetExpansionAll(bool value)
    {
        foreach (var group in Groups)
        {
            group.IsExpanded = value;

            foreach (var child in group.Children)
            {
                SetExpansionRecursive(child, value);
            }
        }
    }

    private static void SetExpansionRecursive(TransferIntentNodeViewModel node, bool value)
    {
        node.IsExpanded = value;

        foreach (var child in node.Children)
        {
            SetExpansionRecursive(child, value);
        }
    }

    [RelayCommand]
    private void ToggleJournal()
    {
        IsJournalOpen = !IsJournalOpen;
    }

    [RelayCommand]
    private void ClearJournal()
    {
        Journal.Clear();
        IsMoreMenuOpen = false;
    }

    partial void OnIsJournalOpenChanged(bool value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.SetValue(JournalOpenSettingsKey, value ? "true" : "false");
    }

    partial void OnStopOnErrorChanged(bool value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.SetValue(StopOnErrorSettingsKey, value ? "true" : "false");
    }

    partial void OnUploadIntervalEnabledChanged(bool value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.SetValue(UploadIntervalEnabledSettingsKey, value ? "true" : "false");
    }

    partial void OnUploadIntervalMinutesTextChanged(string value)
    {
        if (_suppressPersist || ParseUploadInterval(value) is not { } minutes)
        {
            return;
        }

        _settings.SetValue(UploadIntervalMinutesSettingsKey, minutes.ToString(CultureInfo.InvariantCulture));
    }

    private static int? ParseUploadInterval(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
        && minutes is >= 1 and <= MaxUploadIntervalMinutes
            ? minutes
            : null;

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task ExecuteSelectedAsync(CancellationToken externalToken)
    {
        ResyncIntentSelectionFromTree();

        var selectedRoots = CollectSelectedRoots();

        if (selectedRoots.Count == 0)
        {
            return;
        }

        IsRunning = true;
        StatusText = "Старт…";

        ResetRunStatesForRoots(selectedRoots);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _runCts = cts;

        var totalUnits = SelectedSteps > 0 ? SelectedSteps : selectedRoots.Count;
        RunStepsTotal = totalUnits;
        RunStepsCompleted = 0;
        RunStepsFailed = 0;
        var action = _actionHolder.Register(
            $"Передачи: {selectedRoots.Count} цепочек",
            "Старт…",
            totalUnits,
            cts,
            kind: ActionKind.Sync);

        AppendJournal(TransferJournalLevel.Info, $"[Старт] {selectedRoots.Count} цепочек · {totalUnits} шагов");

        var syncErrors = new List<Exception>();
        var cancelled = false;
        var stoppedByError = false;
        var relations = _orcestrator.GetRelations();
        var executor = new ParallelSyncExecutor(selectedRoots, relations);
        var nodeMap = BuildNodeMap();

        try
        {
            using (_actionHolder.BeginScope(action))
            {
                await executor.ExecuteAsync(
                    async intent =>
                    {
                        var node = nodeMap.GetValueOrDefault(intent);

                        StatusText = $"Обработка: {intent.Media.Title}";
                        action.Status = $"Обработка: {intent.Media.Title}";

                        var progress = new Progress<SyncAttemptStatus>(status => HandleAttemptStatus(intent, node, status));

                        await _retryRunner.RunAsync(intent.Media, intent.Relation, progress, cancellationToken: cts.Token);
                        action.ProgressPlus();
                        InvokeOnUi(() => RunStepsCompleted++);

                        if (UploadIntervalEnabled && UploadIntervalMinutes is { } pauseMinutes)
                        {
                            StatusText = $"Пауза {pauseMinutes} мин после загрузки";
                            action.Status = $"Пауза {pauseMinutes} мин после загрузки";
                            AppendJournal(TransferJournalLevel.Info, $"«{intent.Media.Title}» загружено, пауза {pauseMinutes} мин");

                            await Task.Delay(TimeSpan.FromMinutes(pauseMinutes), cts.Token);
                        }
                    },
                    cts.Token,
                    (intent, ex) =>
                    {
                        var node = nodeMap.GetValueOrDefault(intent);
                        InvokeOnUi(() =>
                        {
                            node?.ApplyStatus(new(SyncAttemptKind.FailedFinal, 0, 0, Error: ex));
                            AppendJournal(TransferJournalLevel.Error, $"Ошибка: {intent.Media.Title} – {ex.Message}");
                            RunStepsCompleted++;
                            RunStepsFailed++;
                        });

                        syncErrors.Add(ex);
                        _logger.LogError(ex, "Ошибка передачи {Intent}", intent);

                        if (!StopOnError)
                        {
                            return;
                        }

                        stoppedByError = true;
                        AppendJournal(TransferJournalLevel.Warning, "Прервано из-за ошибки (включён режим «Прервать при ошибке»).");
                        cts.Cancel();
                    });
            }

            StatusText = "Завершено";
            AppendJournal(TransferJournalLevel.Success, "Прогон завершён.");
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            StatusText = stoppedByError ? "Остановлено после ошибки" : "Остановлено";
            AppendJournal(TransferJournalLevel.Warning, stoppedByError ? "Остановлено после ошибки." : "Прогон остановлен пользователем.");
        }
        catch (Exception ex)
        {
            syncErrors.Add(ex);
            _logger.LogError(ex, "Критическая ошибка прогона передач");
            StatusText = $"Критическая ошибка: {ex.Message}";
            AppendJournal(TransferJournalLevel.Error, $"Критическая ошибка: {ex.Message}");
        }
        finally
        {
            if (cancelled || cts.IsCancellationRequested)
            {
                action.MarkCancelled(syncErrors.Count > 0 ? $"Прервано: ошибок {syncErrors.Count}" : "Прервано");
            }
            else if (syncErrors.Count > 0)
            {
                action.Fail($"Ошибок: {syncErrors.Count}", new AggregateException(syncErrors));
            }
            else
            {
                action.Finish("Готово");
            }

            IsRunning = false;
            IsPlanStale = true;
            _runCts = null;
        }
    }

    private bool CanExecute() => !IsRunning && !IsRebuilding && HasAnySelected && HasPlan && IsUploadIntervalValid;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        if (_runCts is null)
        {
            return;
        }

        AppendJournal(TransferJournalLevel.Warning, "Запрошена остановка прогона…");
        _runCts.Cancel();
    }

    private bool CanStop() => IsRunning;

    private List<SyncIntent> CollectSelectedRoots()
    {
        var roots = new List<SyncIntent>();

        foreach (var group in Groups)
        {
            foreach (var child in group.Children)
            {
                if (child.IsSelected)
                {
                    roots.Add(child.Intent);
                }
            }
        }

        return roots;
    }

    private void ResyncIntentSelectionFromTree()
    {
        ClearIntentSelectionRecursive(_allRootIntents);

        foreach (var group in Groups)
        {
            foreach (var node in group.Children)
            {
                ApplyNodeSelectionToIntent(node);
            }
        }
    }

    private static void ClearIntentSelectionRecursive(IEnumerable<SyncIntent> intents)
    {
        foreach (var intent in intents)
        {
            intent.IsSelected = false;
            ClearIntentSelectionRecursive(intent.NextIntents);
        }
    }

    private static void ApplyNodeSelectionToIntent(TransferIntentNodeViewModel node)
    {
        node.Intent.IsSelected = node.IsSelected;

        foreach (var child in node.Children)
        {
            ApplyNodeSelectionToIntent(child);
        }
    }

    internal void MoveGroup(TransferMediaGroupViewModel source, TransferMediaGroupViewModel target, bool insertBelow)
    {
        if (ReferenceEquals(source, target))
        {
            return;
        }

        var sourceIndex = Groups.IndexOf(source);
        var targetIndex = Groups.IndexOf(target);

        if (sourceIndex < 0 || targetIndex < 0)
        {
            return;
        }

        var insertIndex = insertBelow ? targetIndex + 1 : targetIndex;

        if (sourceIndex < insertIndex)
        {
            insertIndex--;
        }

        if (insertIndex == sourceIndex)
        {
            return;
        }

        Groups.Move(sourceIndex, insertIndex);
    }

    internal void OpenInMedia(Media media)
    {
        try
        {
            _navigation.OpenInspector(media);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось открыть медиа {Media} в разделе Медиа", media.Title);
        }
    }

    internal void OpenSourcesGraph(SourceSyncRelation relation)
    {
        try
        {
            _navigation.OpenSourcesGraph(relation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось открыть связь {From} → {To} в разделе Источники и связи",
                relation.From.TitleFull, relation.To.TitleFull);
        }
    }

    internal async Task RunSingleStepAsync(TransferIntentNodeViewModel node)
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        StatusText = $"Точечный прогон: {node.Intent.Media.Title}";

        node.ResetRunState();

        using var cts = new CancellationTokenSource();
        _runCts = cts;

        RunStepsTotal = 1;
        RunStepsCompleted = 0;
        RunStepsFailed = 0;

        var action = _actionHolder.Register(
            $"Передача (одна): {node.Intent.Media.Title}",
            "Старт…",
            1,
            cts,
            kind: ActionKind.Sync);

        AppendJournal(TransferJournalLevel.Info, $"▶ Точечный прогон: {node.Intent.Media.Title} · {node.Route}");

        var failed = false;
        var cancelled = false;
        Exception? error = null;

        try
        {
            using (_actionHolder.BeginScope(action))
            {
                var progress = new Progress<SyncAttemptStatus>(s => HandleAttemptStatus(node.Intent, node, s));
                await _retryRunner.RunAsync(node.Intent.Media, node.Intent.Relation, progress, cancellationToken: cts.Token);
                action.ProgressPlus();
                RunStepsCompleted = 1;
            }

            StatusText = "Завершено";
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            StatusText = "Остановлено";
            AppendJournal(TransferJournalLevel.Warning, "Точечный прогон остановлен.");
        }
        catch (Exception ex)
        {
            failed = true;
            error = ex;
            RunStepsCompleted = 1;
            RunStepsFailed = 1;
            _logger.LogError(ex, "Ошибка точечного прогона {Intent}", node.Intent);
            StatusText = $"Ошибка: {ex.Message}";
            AppendJournal(TransferJournalLevel.Error, $"Ошибка точечного прогона: {ex.Message}");
        }
        finally
        {
            if (cancelled || cts.IsCancellationRequested)
            {
                action.MarkCancelled("Прервано");
            }
            else if (failed)
            {
                action.Fail("Ошибка: " + error?.Message, error);
            }
            else
            {
                action.Finish("Готово");
            }

            IsRunning = false;
            IsPlanStale = true;
            _runCts = null;
        }
    }

    private Dictionary<SyncIntent, TransferIntentNodeViewModel> BuildNodeMap()
    {
        var map = new Dictionary<SyncIntent, TransferIntentNodeViewModel>();

        foreach (var group in Groups)
        {
            foreach (var child in group.Children)
            {
                AddNodeRecursive(child, map);
            }
        }

        return map;
    }

    private static void AddNodeRecursive(TransferIntentNodeViewModel node, Dictionary<SyncIntent, TransferIntentNodeViewModel> map)
    {
        map[node.Intent] = node;

        foreach (var child in node.Children)
        {
            AddNodeRecursive(child, map);
        }
    }

    private void ResetRunStatesForRoots(List<SyncIntent> roots)
    {
        var nodeMap = BuildNodeMap();

        foreach (var root in roots)
        {
            if (!nodeMap.TryGetValue(root, out var node))
            {
                continue;
            }

            ResetRecursive(node);
        }
    }

    private static void ResetRecursive(TransferIntentNodeViewModel node)
    {
        node.ResetRunState();

        foreach (var child in node.Children)
        {
            ResetRecursive(child);
        }
    }

    private void HandleAttemptStatus(SyncIntent intent, TransferIntentNodeViewModel? node, SyncAttemptStatus status)
    {
        InvokeOnUi(() =>
        {
            node?.ApplyStatus(status);

            switch (status.Kind)
            {
                case SyncAttemptKind.Started when status.AttemptNumber == 1:
                    AppendJournal(TransferJournalLevel.Info, $"▶ {intent.Media.Title} · {intent.From.TypeId} → {intent.To.TypeId}");
                    break;

                case SyncAttemptKind.Succeeded:
                    AppendJournal(TransferJournalLevel.Success, $"✓ {intent.Media.Title} → {intent.To.Title}");

                    if (node is not null)
                    {
                        node.SetIsSelectedSilent(false);
                        FindGroupOf(node)?.Recompute();
                        RecomputeCounts();
                    }

                    break;

                case SyncAttemptKind.FailedRetrying:
                {
                    var minutes = status.NextDelay?.TotalMinutes ?? 0;
                    AppendJournal(
                        TransferJournalLevel.Warning,
                        $"⚠ {intent.Media.Title}: {status.Error?.Message}. Попытка {status.AttemptNumber + 1}/{status.MaxAttempts} через {minutes:F0} мин");
                    break;
                }

                case SyncAttemptKind.NonRetriable:
                    AppendJournal(TransferJournalLevel.Error, $"⛔ {intent.Media.Title}: {status.Error?.Message} (не ретраим)");
                    break;

                case SyncAttemptKind.Joined:
                    AppendJournal(TransferJournalLevel.Info, $"⏳ {intent.Media.Title}: операция уже выполняется, ждём");
                    break;
            }
        });
    }

    private void AppendJournal(TransferJournalLevel level, string text)
    {
        InvokeOnUi(() =>
        {
            Journal.Insert(0, new(DateTime.Now, level, text));

            while (Journal.Count > JournalMaxEntries)
            {
                Journal.RemoveAt(Journal.Count - 1);
            }
        });
    }

    private static void InvokeOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }
}
