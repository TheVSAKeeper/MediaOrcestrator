using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class TransfersViewModel : ObservableObject, IPageHeader, IPageRenderMetric, IPageEscapeHandler, IPageSearchable
{
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
    private readonly IUiDispatcher _dispatcher;
    private readonly IPageRenderProbe _renderProbe;

    private readonly List<SyncIntent> _allRootIntents = [];
    private readonly IUiTimer _searchTimer;
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
    [NotifyPropertyChangedFor(nameof(CanHandleEscape))]
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
        ILogger<TransfersViewModel> logger,
        IUiDispatcher dispatcher,
        IPageRenderProbe renderProbe)
    {
        _orcestrator = orcestrator;
        _planner = planner;
        _retryRunner = retryRunner;
        _actionHolder = actionHolder;
        _settings = settings;
        _navigation = navigation;
        _logger = logger;
        _dispatcher = dispatcher;
        _renderProbe = renderProbe;

        Groups = [];
        Journal = [];
        SourceFilters = [];
        RelationFilters = [];

        _searchTimer = dispatcher.CreateTimer(TimeSpan.FromMilliseconds(SearchDebounceMs), OnSearchTimerTick);

        _suppressPersist = true;
        IsJournalOpen = bool.TryParse(_settings.GetStringValue(JournalOpenSettingsKey), out var open) && open;
        StopOnError = !bool.TryParse(_settings.GetStringValue(StopOnErrorSettingsKey), out var stop) || stop;
        UploadIntervalEnabled = bool.TryParse(_settings.GetStringValue(UploadIntervalEnabledSettingsKey), out var interval) && interval;
        UploadIntervalMinutesText = (ParseUploadInterval(_settings.GetStringValue(UploadIntervalMinutesSettingsKey)) ?? DefaultUploadIntervalMinutes)
            .ToString(CultureInfo.InvariantCulture);
        _suppressPersist = false;
    }

    public event EventHandler? SearchFocusRequested;

    public string PageTitle => "Передачи";

    public string? PageDescription =>
        "Перенос медиа между источниками по заданным связям. План строится автоматически, запуск – ручной.";

    string? IPageRenderMetric.RenderDurationText => RenderDurationText;

    IReadOnlyList<PageRenderStage>? IPageRenderMetric.RenderStages => RenderStages;

    public bool CanHandleEscape => IsJournalOpen;

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

    public bool TryHandleEscape()
    {
        if (!IsJournalOpen)
        {
            return false;
        }

        IsJournalOpen = false;
        return true;
    }

    public void RequestSearchFocus() => SearchFocusRequested?.Invoke(this, EventArgs.Empty);

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

    private void PublishRenderMetric(IReadOnlyList<PageRenderStage> vmStages, long vmMs)
    {
        _renderProbe.MeasureNextFrame(renderMs => ApplyMetric(vmStages, vmMs, renderMs));
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
}
