using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed partial class CommentsViewModel : ObservableObject, ICommentItemHost, IPageStatus, IPageHeader, IPageRenderMetric, IPageSearchable
{
    private const int SearchDebounceMs = 300;
    private const string FiltersExpandedKey = "wpf.comments.filters_expanded";

    private readonly Orcestrator _orcestrator;
    private readonly CommentsService _commentsService;
    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<CommentsViewModel> _logger;
    private readonly IShellLauncher _shellLauncher;
    private readonly IUiDispatcher _dispatcher;
    private readonly IPageRenderProbe? _renderProbe;
    private readonly CommentsTreeBuilder _treeBuilder;
    private readonly CommentsSourceFetcher _sourceFetcher;
    private readonly CommentsSnapshotCache _snapshotCache = new();

    private readonly IUiTimer _searchTimer;
    private readonly IUiTimer _relativeTimeTimer;
    private readonly object _itemsLock = new();
    private readonly HashSet<string> _expandedBranches = new(StringComparer.Ordinal);
    private readonly Dictionary<(string SourceId, string ExternalMediaId), IReadOnlyList<CommentAuthorOptionViewModel>> _authorsCache = new();
    private readonly HashSet<(string SourceId, string ExternalMediaId)> _authorsInFlight = new();

    private CommentsViewSettings _settings = new();
    private int _applyVersion;
    private CancellationTokenSource? _applyCts;
    private Task? _lastApplyTask;
    private bool _loaded;
    private bool _isViewVisible = true;
    private bool _suppressPersist;
    private ReplyEditorViewModel? _activeReplyEditor;
    private CancellationTokenSource? _fetchCts;
    private List<ICommentsRowViewModel>? _automationStash;
    private string? _automationEmptyMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnySources))]
    private bool _hasSources;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchSelectedCommand))]
    [NotifyPropertyChangedFor(nameof(CanFetch))]
    [NotifyPropertyChangedFor(nameof(SelectedSourcesText))]
    private int _selectedSourceCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFetch))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(FetchSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelFetchCommand))]
    private bool _isFetching;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LayoutIsGrouped))]
    private CommentLayoutOption _selectedLayout;

    [ObservableProperty]
    private CommentReplyStatusOption _selectedReplyStatus;

    [ObservableProperty]
    private int _limit = 1000;

    [ObservableProperty]
    private string _totalCountText = string.Empty;

    [ObservableProperty]
    private string? _renderDurationText;

    [ObservableProperty]
    private IReadOnlyList<PageRenderStage>? _renderStages;

    [ObservableProperty]
    private string? _truncatedHint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyMessage))]
    private string? _emptyMessage;

    [ObservableProperty]
    private string _emptyHeading = "Комментариев нет";

    [ObservableProperty]
    private PackIconLucideKind _emptyIcon = PackIconLucideKind.MessageSquare;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyMessage))]
    private string? _loadError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    private string? _fetchProgressMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressValue))]
    private int _fetchProgressValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressMax))]
    private int _fetchProgressMax;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    private string? _fetchCounterText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    private bool _fetchProgressIsIndeterminate;

    [ObservableProperty]
    private bool _isFiltersExpanded = true;

    public CommentsViewModel(
        Orcestrator orcestrator,
        CommentsService commentsService,
        ActionHolder actionHolder,
        IDialogService dialogs,
        ISettingsStore settingsStore,
        ILogger<CommentsViewModel> logger,
        IShellLauncher shellLauncher,
        IUiDispatcher dispatcher,
        IPageRenderProbe? renderProbe = null)
    {
        _orcestrator = orcestrator;
        _commentsService = commentsService;
        _actionHolder = actionHolder;
        _dialogs = dialogs;
        _settingsStore = settingsStore;
        _logger = logger;
        _shellLauncher = shellLauncher;
        _dispatcher = dispatcher;
        _renderProbe = renderProbe;
        _treeBuilder = new(orcestrator, dispatcher, logger);
        _sourceFetcher = new(orcestrator, commentsService, actionHolder, dialogs, logger, shellLauncher, dispatcher);

        _isFiltersExpanded = _settingsStore.GetBool(FiltersExpandedKey, fallback: true);

        Items = [];
        Sources = [];

        Sources.CollectionChanged += OnSourcesCollectionChanged;

        LayoutOptions =
        [
            new(CommentsLayoutMode.Flat, "Список"),
            new(CommentsLayoutMode.Grouped, "По медиа"),
        ];

        ReplyStatusOptions =
        [
            new(CommentsReplyStatusFilter.WithoutReplyAndLike, "Без ответа и лайка"),
            new(CommentsReplyStatusFilter.All, "Все"),
            new(CommentsReplyStatusFilter.WithoutReply, "Без ответа"),
            new(CommentsReplyStatusFilter.WithReply, "С ответом"),
            new(CommentsReplyStatusFilter.NewReplies, "Новые ответы"),
        ];

        _selectedLayout = LayoutOptions[0];
        _selectedReplyStatus = ReplyStatusOptions[0];

        _searchTimer = dispatcher.CreateTimer(TimeSpan.FromMilliseconds(SearchDebounceMs), OnSearchTimerTick);
        _relativeTimeTimer = dispatcher.CreateTimer(TimeSpan.FromMinutes(1), RefreshRelativeTimes);
    }

    public event EventHandler? SearchFocusRequested;

    public IUiDispatcher Dispatcher => _dispatcher;

    public string PageTitle => "Комментарии";

    public string? PageDescription =>
        "Сводка комментариев под медиа всех источников. Поддерживаются ответы, правка, удаление и лайки – в зависимости от возможностей источника.";

    string? IPageRenderMetric.RenderDurationText => RenderDurationText;

    IReadOnlyList<PageRenderStage>? IPageRenderMetric.RenderStages => RenderStages;

    public ResetableObservableCollection<ICommentsRowViewModel> Items { get; }

    public RangeObservableCollection<CommentSourceFilterOptionViewModel> Sources { get; }

    public IReadOnlyList<CommentLayoutOption> LayoutOptions { get; }

    public IReadOnlyList<CommentReplyStatusOption> ReplyStatusOptions { get; }

    public bool HasAnySources => Sources.Count > 0;

    public bool HasLoadError => LoadError is not null;

    public bool ShowEmptyMessage => EmptyMessage is not null && !HasLoadError;

    public bool LayoutIsGrouped => SelectedLayout.Mode == CommentsLayoutMode.Grouped;

    public bool CanFetch => !IsFetching && SelectedSourceCount > 0;

    public string SelectedSourcesText => SelectedSourceCount switch
    {
        0 => "Источники (все)",
        1 => $"Источник: {Sources.FirstOrDefault(s => s.IsChecked)?.Title ?? "–"}",
        _ => $"Источников выбрано: {SelectedSourceCount}",
    };

    public bool IsBusy => IsFetching;

    public double ProgressValue => FetchProgressValue;

    public double ProgressMax => FetchProgressMax;

    public bool IsIndeterminate => FetchProgressIsIndeterminate;

    public ICommand? CancelCommand => CancelFetchCommand;

    public string? StatusCaption =>
        (FetchProgressMessage, FetchCounterText) switch
        {
            (null or "", null or "") => null,
            (var m, null or "") => m,
            (null or "", var c) => c,
            (var m, var c) => $"{m}  ·  {c}",
        };

    public void RequestSearchFocus()
    {
        IsFiltersExpanded = true;
        SearchFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _suppressPersist = true;

        try
        {
            _settings = await Task.Run(() => CommentsViewSettings.Load(_settingsStore));
            ApplySettingsToProperties();
            ReloadSources();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить настройки и источники комментариев");
            LoadError = ex.Message;
            return;
        }
        finally
        {
            _suppressPersist = false;
        }

        ScheduleApplyFilters();

        if (_isViewVisible)
        {
            _relativeTimeTimer.Start();
        }
    }

    public void SetViewVisible(bool isVisible)
    {
        _isViewVisible = isVisible;

        if (!isVisible)
        {
            _relativeTimeTimer.Stop();
            return;
        }

        if (_loaded)
        {
            RefreshRelativeTimes();
            _relativeTimeTimer.Start();
        }
    }

    private void RefreshRelativeTimes()
    {
        foreach (var row in Items)
        {
            if (row is CommentItemViewModel item)
            {
                item.RefreshRelativeTime();
            }
        }
    }

    private void OnSearchTimerTick()
    {
        _searchTimer.Stop();
        PersistSettings();
        ScheduleApplyFilters();
    }

    [RelayCommand]
    private void ReloadSourcesCommand()
    {
        LoadError = null;
        InvalidateSnapshotCache();

        try
        {
            ReloadSources();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось обновить список источников комментариев");
            LoadError = ex.Message;
            return;
        }

        ScheduleApplyFilters();
    }

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchSelectedAsync()
    {
        if (IsFetching || SelectedSourceCount == 0)
        {
            return;
        }

        var dialog = new CommentsFetchOptionsDialogViewModel(_settings);

        if (!await _dialogs.ShowAsync(dialog))
        {
            return;
        }

        _settings.FetchSinceDays = dialog.SinceDays;
        _settings.FetchOnlyRecent = dialog.OnlyRecent;
        _settings.FetchMaxParallelism = dialog.MaxParallelism;
        PersistSettings();

        var selectedIds = Sources.Where(s => s.IsChecked).Select(s => s.SourceId).ToHashSet(StringComparer.Ordinal);
        var selected = _orcestrator.GetSources().Where(s => selectedIds.Contains(s.Id)).ToList();

        if (selected.Count == 0)
        {
            return;
        }

        IsFetching = true;
        _fetchCts = new();
        FetchProgressIsIndeterminate = false;

        try
        {
            await _sourceFetcher.FetchFromSourcesAsync(selected, _settings, _fetchCts, p =>
            {
                FetchProgressMax = p.Total;
                FetchProgressValue = Math.Min(p.Processed, p.Total);
                FetchProgressMessage = p.Message;
                FetchCounterText = p.Counter;
            });
        }
        finally
        {
            IsFetching = false;
            FetchProgressMessage = null;
            FetchCounterText = null;
            FetchProgressValue = 0;
            FetchProgressMax = 0;
            _fetchCts?.Dispose();
            _fetchCts = null;
            InvalidateSnapshotCache();
            ScheduleApplyFilters();
        }
    }

    [RelayCommand(CanExecute = nameof(IsFetching))]
    private void CancelFetch() => _fetchCts?.Cancel();

    [RelayCommand]
    private async Task OpenAppearanceAsync()
    {
        var dialog = new CommentsAppearanceDialogViewModel(_settings.Appearance, _settings.ReplyPrefixTemplate, _settings.LayoutMode);

        if (!await _dialogs.ShowAsync(dialog))
        {
            return;
        }

        _settings.Appearance = dialog.Snapshot();
        _settings.ReplyPrefixTemplate = dialog.ReplyPrefixTemplate;

        var newLayout = dialog.SelectedLayoutMode;
        var layoutChanged = newLayout != _settings.LayoutMode;
        _settings.LayoutMode = newLayout;

        if (layoutChanged)
        {
            SelectedLayout = LayoutOptions.FirstOrDefault(o => o.Mode == newLayout) ?? LayoutOptions[0];
        }

        PersistSettings();
        CommentsAppearanceResources.Apply(_settings.Appearance);
    }

    internal void SetEmptyForAutomation(bool empty)
    {
        if (empty)
        {
            _automationStash ??= [.. Items];
            _automationEmptyMessage ??= EmptyMessage;
            Items.ReplaceAll([]);
            EmptyHeading = "Комментариев нет";
            EmptyMessage = "Под текущие фильтры ничего не подошло. Сбросьте поиск или статус ответа.";
        }
        else if (_automationStash is not null)
        {
            Items.ReplaceAll(_automationStash);
            EmptyMessage = _automationEmptyMessage;
            _automationStash = null;
            _automationEmptyMessage = null;
        }
    }
}
