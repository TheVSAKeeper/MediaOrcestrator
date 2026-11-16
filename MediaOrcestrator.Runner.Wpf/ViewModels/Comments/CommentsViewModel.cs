using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed partial class CommentsViewModel : ObservableObject, ICommentItemHost, IPageStatus, IPageHeader, IPageRenderMetric, IPageSearchable
{
    public event EventHandler? SearchFocusRequested;

    public void RequestSearchFocus()
    {
        IsFiltersExpanded = true;
        SearchFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public string PageTitle => "Комментарии";

    public string? PageDescription =>
        "Сводка комментариев под медиа всех источников. Поддерживаются ответы, правка, удаление и лайки – в зависимости от возможностей источника.";

    string? IPageRenderMetric.RenderDurationText => RenderDurationText;

    IReadOnlyList<PageRenderStage>? IPageRenderMetric.RenderStages => RenderStages;


    private const int SearchDebounceMs = 300;
    private const int VisibleRepliesPerRoot = 2;
    private const int BranchToggleThreshold = 3;
    private const string FiltersExpandedKey = "wpf.comments.filters_expanded";

    private readonly Orcestrator _orcestrator;
    private readonly CommentsService _commentsService;
    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<CommentsViewModel> _logger;
    private readonly IShellLauncher _shellLauncher;

    private readonly DispatcherTimer _searchTimer;
    private readonly DispatcherTimer _relativeTimeTimer;
    private readonly object _itemsLock = new();
    private readonly HashSet<string> _expandedBranches = new(StringComparer.Ordinal);
    private readonly Dictionary<(string SourceId, string ExternalMediaId), IReadOnlyList<CommentAuthorOptionViewModel>> _authorsCache = new();
    private readonly HashSet<(string SourceId, string ExternalMediaId)> _authorsInFlight = new();

    private CommentsViewSettings _settings = new();
    private int _applyVersion;
    private CancellationTokenSource? _applyCts;
    private bool _loaded;
    private bool _isViewVisible = true;
    private bool _suppressPersist;
    private ReplyEditorViewModel? _activeReplyEditor;

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

    private CancellationTokenSource? _fetchCts;

    public CommentsViewModel(
        Orcestrator orcestrator,
        CommentsService commentsService,
        ActionHolder actionHolder,
        IDialogService dialogs,
        ISettingsStore settingsStore,
        ILogger<CommentsViewModel> logger,
        IShellLauncher shellLauncher)
    {
        _orcestrator = orcestrator;
        _commentsService = commentsService;
        _actionHolder = actionHolder;
        _dialogs = dialogs;
        _settingsStore = settingsStore;
        _logger = logger;
        _shellLauncher = shellLauncher;

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

        _searchTimer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(SearchDebounceMs),
        };

        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            PersistSettings();
            ScheduleApplyFilters();
        };

        _relativeTimeTimer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(1),
        };

        _relativeTimeTimer.Tick += (_, _) => RefreshRelativeTimes();
    }

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

    [RelayCommand]
    private void ReloadSourcesCommand()
    {
        LoadError = null;

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
        var ok = await _dialogs.ShowAsync(dialog);

        if (!ok)
        {
            return;
        }

        _settings.FetchSinceDays = dialog.SinceDays;
        _settings.FetchOnlyRecent = dialog.OnlyRecent;
        _settings.FetchMaxParallelism = dialog.MaxParallelism;
        PersistSettings();

        var selectedIds = Sources.Where(s => s.IsChecked).Select(s => s.SourceId).ToHashSet(StringComparer.Ordinal);
        var allSources = _orcestrator.GetSources();
        var selected = allSources.Where(s => selectedIds.Contains(s.Id)).ToList();

        if (selected.Count == 0)
        {
            return;
        }

        await FetchFromSourcesAsync(selected);
    }

    [RelayCommand(CanExecute = nameof(IsFetching))]
    private void CancelFetch()
    {
        _fetchCts?.Cancel();
    }

    [RelayCommand]
    private async Task OpenAppearanceAsync()
    {
        var dialog = new CommentsAppearanceDialogViewModel(_settings.Appearance, _settings.ReplyPrefixTemplate, _settings.LayoutMode);
        var ok = await _dialogs.ShowAsync(dialog);

        if (!ok)
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

    [RelayCommand]
    private void ResetFilters()
    {
        SearchText = string.Empty;
        SelectedReplyStatus = ReplyStatusOptions[0];

        foreach (var source in Sources)
        {
            source.IsChecked = false;
        }

        PersistSettings();
        ScheduleApplyFilters();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    partial void OnLimitChanged(int value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.Limit = Math.Clamp(value, 100, 10000);
        PersistSettings();
        ScheduleApplyFilters();
    }

    partial void OnSelectedLayoutChanged(CommentLayoutOption value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.LayoutMode = value.Mode;
        PersistSettings();
        ScheduleApplyFilters();
    }

    partial void OnSelectedReplyStatusChanged(CommentReplyStatusOption value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.ReplyStatus = value.Key;
        PersistSettings();
        ScheduleApplyFilters();
    }

    partial void OnIsFiltersExpandedChanged(bool value)
    {
        _settingsStore.SetBool(FiltersExpandedKey, value);
    }

    private void ApplySettingsToProperties()
    {
        SearchText = _settings.Search;
        Limit = Math.Clamp(_settings.Limit <= 0 ? 1000 : _settings.Limit, 100, 10000);

        SelectedLayout = LayoutOptions.FirstOrDefault(o => o.Mode == _settings.LayoutMode) ?? LayoutOptions[0];
        SelectedReplyStatus = ReplyStatusOptions.FirstOrDefault(o => o.Key == _settings.ReplyStatus) ?? ReplyStatusOptions[0];

        CommentsAppearanceResources.Apply(_settings.Appearance);
    }

    private void ReloadSources()
    {
        var sources = _orcestrator.GetSources();
        var supportsComments = sources
            .Where(s => s.Type is ISupportsComments)
            .OrderBy(s => s.TitleFull, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var checkedIds = new HashSet<string>(_settings.SelectedSourceIds, StringComparer.Ordinal);

        foreach (var existing in Sources)
        {
            existing.PropertyChanged -= OnSourceOptionChanged;
        }

        var newSources = supportsComments
            .Select(src =>
            {
                var option = new CommentSourceFilterOptionViewModel(src.Id, src.TitleFull)
                {
                    IsChecked = checkedIds.Contains(src.Id),
                };
                option.PropertyChanged += OnSourceOptionChanged;
                return option;
            })
            .ToList();

        Sources.ReplaceAll(newSources);

        HasSources = Sources.Count > 0;
        SelectedSourceCount = Sources.Count(s => s.IsChecked);
    }

    private void OnSourceOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommentSourceFilterOptionViewModel.IsChecked))
        {
            return;
        }

        SelectedSourceCount = Sources.Count(s => s.IsChecked);

        if (_suppressPersist)
        {
            return;
        }

        _settings.SelectedSourceIds = Sources.Where(s => s.IsChecked).Select(s => s.SourceId).ToList();
        PersistSettings();
        ScheduleApplyFilters();
    }

    private void OnSourcesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasAnySources));
    }

    private void PersistSettings()
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.Search = SearchText;
        _settings.Limit = Limit;
        _settings.LayoutMode = SelectedLayout.Mode;
        _settings.ReplyStatus = SelectedReplyStatus.Key;
        _settings.SelectedSourceIds = Sources.Where(s => s.IsChecked).Select(s => s.SourceId).ToList();

        var snapshot = _settings;
        _ = Task.Run(() => snapshot.Save(_settingsStore));
    }

    private void ScheduleApplyFilters()
    {
        if (!_loaded)
        {
            return;
        }

        _ = ApplyFiltersAsync();
    }

    private async Task ApplyFiltersAsync()
    {
        var version = Interlocked.Increment(ref _applyVersion);

        _applyCts?.Cancel();
        _applyCts?.Dispose();
        _applyCts = new();
        var ct = _applyCts.Token;

        var selectedSourceIds = Sources.Where(s => s.IsChecked).Select(s => s.SourceId).ToHashSet(StringComparer.Ordinal);
        var search = (SearchText ?? string.Empty).Trim();
        var limit = Math.Clamp(Limit, 100, 10000);
        var layout = SelectedLayout.Mode;
        var replyStatus = SelectedReplyStatus.Key;

        IsLoading = true;
        LoadError = null;

        var totalSw = Stopwatch.StartNew();
        long buildMs = 0;

        try
        {
            var buildSw = Stopwatch.StartNew();
            var rows = await Task.Run(() => BuildItems(selectedSourceIds, search, limit, layout, replyStatus, ct), ct);
            buildSw.Stop();
            buildMs = buildSw.ElapsedMilliseconds;

            if (version != Volatile.Read(ref _applyVersion))
            {
                return;
            }

            ReplaceItems(rows);

            totalSw.Stop();
            var uiMs = totalSw.ElapsedMilliseconds - buildMs;
            RenderDurationText = $"{totalSw.ElapsedMilliseconds} мс";
            RenderStages =
            [
                new("Подготовка данных", $"{buildMs} мс"),
                new("Применение к UI", $"{uiMs} мс"),
                new("Итого", $"{totalSw.ElapsedMilliseconds} мс"),
                new("Строк", rows.Rows.Count.ToString()),
            ];
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка применения фильтров комментариев");

            if (version == Volatile.Read(ref _applyVersion))
            {
                LoadError = ex.Message;
            }
        }
        finally
        {
            if (version == Volatile.Read(ref _applyVersion))
            {
                IsLoading = false;
            }
        }
    }

    private BuildItemsResult BuildItems(
        HashSet<string> selectedSourceIds,
        string search,
        int limit,
        CommentsLayoutMode layout,
        CommentsReplyStatusFilter replyStatus,
        CancellationToken ct)
    {
        var sourceIds = selectedSourceIds.Count == 0 ? null : (IReadOnlyCollection<string>)selectedSourceIds;
        var records = LoadRecords(sourceIds, search, limit + 1, ct);

        var truncated = records.Count > limit;

        if (truncated)
        {
            records = records.Take(limit).ToList();
        }

        BackfillOrphanParents(records, ct);
        records = FilterByReplyStatus(records, replyStatus);

        ct.ThrowIfCancellationRequested();

        return layout == CommentsLayoutMode.Grouped
            ? BuildGrouped(records, truncated, search)
            : BuildFlat(records, truncated, search);
    }

    private List<CommentRecord> LoadRecords(IReadOnlyCollection<string>? sourceIds, string search, int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(search))
        {
            return _commentsService.Query(sourceIds, limit: limit);
        }

        var byText = _commentsService.Query(sourceIds, textContains: search, limit: limit);

        ct.ThrowIfCancellationRequested();

        var sourceFilter = sourceIds is { Count: > 0 } sids
            ? new HashSet<string>(sids, StringComparer.Ordinal)
            : null;

        var titleMatches = _orcestrator.GetMedias()
            .Where(m => !string.IsNullOrEmpty(m.Title) && m.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
            .SelectMany(m => m.Sources)
            .Where(l => !string.IsNullOrEmpty(l.SourceId) && !string.IsNullOrEmpty(l.ExternalId))
            .Where(l => sourceFilter is null || sourceFilter.Contains(l.SourceId))
            .Select(l => (l.SourceId, l.ExternalId))
            .Distinct()
            .ToList();

        if (titleMatches.Count == 0)
        {
            return byText;
        }

        var seen = new HashSet<string>(byText.Select(r => r.Id), StringComparer.Ordinal);
        var combined = new List<CommentRecord>(byText);

        foreach (var (sId, externalId) in titleMatches)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var record in _commentsService.GetByMedia(sId, externalId))
            {
                if (seen.Add(record.Id))
                {
                    combined.Add(record);
                }
            }
        }

        return combined;
    }

    private void BackfillOrphanParents(List<CommentRecord> records, CancellationToken ct)
    {
        if (records.Count == 0)
        {
            return;
        }

        var known = new HashSet<string>(records.Select(r => r.Id), StringComparer.Ordinal);
        var pending = new Queue<string>();

        foreach (var r in records)
        {
            if (string.IsNullOrEmpty(r.ParentExternalCommentId))
            {
                continue;
            }

            var parentId = $"{r.SourceId}|{r.ExternalMediaId}|{r.ParentExternalCommentId}";

            if (known.Add(parentId))
            {
                pending.Enqueue(parentId);
            }
        }

        var safety = records.Count * 2;

        while (pending.Count > 0 && safety-- > 0)
        {
            ct.ThrowIfCancellationRequested();

            var parentId = pending.Dequeue();
            var parent = _commentsService.GetById(parentId);

            if (parent is null)
            {
                continue;
            }

            records.Add(parent);

            if (string.IsNullOrEmpty(parent.ParentExternalCommentId))
            {
                continue;
            }

            var gp = $"{parent.SourceId}|{parent.ExternalMediaId}|{parent.ParentExternalCommentId}";

            if (known.Add(gp))
            {
                pending.Enqueue(gp);
            }
        }
    }

    private static List<CommentRecord> FilterByReplyStatus(List<CommentRecord> records, CommentsReplyStatusFilter filter)
    {
        if (filter == CommentsReplyStatusFilter.All || records.Count == 0)
        {
            return records;
        }

        var byId = new Dictionary<string, CommentRecord>(records.Count, StringComparer.Ordinal);
        foreach (var r in records)
        {
            byId.TryAdd(r.Id, r);
        }

        var childrenByParent = new Dictionary<string, List<CommentRecord>>(StringComparer.Ordinal);

        foreach (var r in records)
        {
            if (string.IsNullOrEmpty(r.ParentExternalCommentId))
            {
                continue;
            }

            var parentId = $"{r.SourceId}|{r.ExternalMediaId}|{r.ParentExternalCommentId}";

            if (!byId.ContainsKey(parentId))
            {
                continue;
            }

            if (!childrenByParent.TryGetValue(parentId, out var bucket))
            {
                bucket = [];
                childrenByParent[parentId] = bucket;
            }

            bucket.Add(r);
        }

        var result = new List<CommentRecord>();

        foreach (var root in records)
        {
            if (!IsRoot(root, byId) || root.IsAuthor)
            {
                continue;
            }

            var descendants = new List<CommentRecord>();
            CollectDescendants(root, childrenByParent, descendants);

            if (!MatchesReplyStatus(root, descendants, filter))
            {
                continue;
            }

            result.Add(root);
            result.AddRange(descendants);
        }

        return result;
    }

    private static bool IsRoot(CommentRecord r, Dictionary<string, CommentRecord> byId)
    {
        if (string.IsNullOrEmpty(r.ParentExternalCommentId))
        {
            return true;
        }

        var parentId = $"{r.SourceId}|{r.ExternalMediaId}|{r.ParentExternalCommentId}";
        return !byId.ContainsKey(parentId);
    }

    private static void CollectDescendants(
        CommentRecord node,
        Dictionary<string, List<CommentRecord>> childrenByParent,
        List<CommentRecord> output)
    {
        if (!childrenByParent.TryGetValue(node.Id, out var kids))
        {
            return;
        }

        foreach (var kid in kids)
        {
            output.Add(kid);
            CollectDescendants(kid, childrenByParent, output);
        }
    }

    private static bool MatchesReplyStatus(CommentRecord root, List<CommentRecord> descendants, CommentsReplyStatusFilter filter)
    {
        DateTime? latestAuthorReply = null;

        foreach (var d in descendants)
        {
            if (d.IsAuthor && (latestAuthorReply == null || d.PublishedAt > latestAuthorReply))
            {
                latestAuthorReply = d.PublishedAt;
            }
        }

        var hasAuthorReply = latestAuthorReply != null;

        return filter switch
        {
            CommentsReplyStatusFilter.WithoutReply => !hasAuthorReply,
            CommentsReplyStatusFilter.WithReply => hasAuthorReply,
            CommentsReplyStatusFilter.NewReplies => hasAuthorReply
                                                    && descendants.Any(d => !d.IsAuthor && d.PublishedAt > latestAuthorReply!.Value),
            CommentsReplyStatusFilter.WithoutReplyAndLike => !hasAuthorReply && !root.LikedByAuthor,
            _ => true,
        };
    }

    private BuildItemsResult BuildFlat(List<CommentRecord> records, bool truncated, string search)
    {
        var sourceById = _orcestrator.GetSources().ToDictionary(s => s.Id);
        var mediaByPair = BuildMediaIndex();

        var byId = records.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var childrenByParent = BuildChildrenIndex(records, byId);

        var rows = new List<ICommentsRowViewModel>();

        var sortedRoots = records
            .Where(r => IsRoot(r, byId))
            .OrderByDescending(r => r.PublishedAt)
            .ToList();

        foreach (var root in sortedRoots)
        {
            var rootDepth = 0;
            AppendItem(rows, root, rootDepth, sourceById, mediaByPair, childrenByParent, search, showMediaThumbnail: true, CommentsLayoutMode.Flat);
            AppendChildren(rows, root, 1, sourceById, mediaByPair, childrenByParent, search, showMediaThumbnail: false, CommentsLayoutMode.Flat);
        }

        return new(rows, records.Count, truncated);
    }

    private BuildItemsResult BuildGrouped(List<CommentRecord> records, bool truncated, string search)
    {
        var sourceById = _orcestrator.GetSources().ToDictionary(s => s.Id);
        var mediaByPair = BuildMediaIndex();

        var byId = records.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var childrenByParent = BuildChildrenIndex(records, byId);

        var groups = records
            .GroupBy(r => ResolveGroupKey(r, mediaByPair))
            .Select(g =>
            {
                var sample = g.First();
                var media = mediaByPair.TryGetValue((sample.SourceId, sample.ExternalMediaId), out var info) ? info.Media : null;
                var title = media?.Title ?? $"<{sample.ExternalMediaId}>";
                var sortNumber = media is null
                    ? int.MaxValue
                    : g.Min(r => mediaByPair.TryGetValue((r.SourceId, r.ExternalMediaId), out var info2) ? info2.SortNumber : int.MaxValue);

                return new
                {
                    Key = g.Key,
                    Title = title,
                    Records = g.ToList(),
                    SortNumber = sortNumber,
                    Media = media,
                };
            })
            .OrderByDescending(g => g.SortNumber)
            .ThenByDescending(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<ICommentsRowViewModel>();

        foreach (var grp in groups)
        {
            var sources = grp.Records
                .GroupBy(r => r.SourceId)
                .Select(s =>
                {
                    var src = sourceById.GetValueOrDefault(s.Key);
                    var sourceTitle = src?.TitleFull ?? s.Key;
                    var firstExt = s.First().ExternalMediaId;
                    var hasExternal = HasMediaExternalLink(sourceById, s.Key, firstExt);
                    var hasFetch = src?.Type is ISupportsComments;

                    return new MediaGroupSourceInfo(s.Key, firstExt, sourceTitle, hasExternal, hasFetch);
                })
                .OrderBy(s => s.SourceTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var thumbnailUrl = ResolveThumbnailUrl(grp.Media);
            rows.Add(new MediaGroupHeaderViewModel(
                grp.Key,
                grp.Title,
                grp.Records.Count,
                sources,
                RefreshGroupAsync,
                RefreshOneSourceAsync,
                OpenOneSourceAsync,
                thumbnailUrl));

            var groupByIdLocal = grp.Records.ToDictionary(r => r.Id, StringComparer.Ordinal);
            var groupChildrenLocal = BuildChildrenIndex(grp.Records, groupByIdLocal);

            var groupRoots = grp.Records
                .Where(r => IsRoot(r, groupByIdLocal))
                .OrderByDescending(r => r.PublishedAt)
                .ToList();

            foreach (var root in groupRoots)
            {
                AppendItem(rows, root, 0, sourceById, mediaByPair, groupChildrenLocal, search, showMediaThumbnail: false, CommentsLayoutMode.Grouped);
                AppendChildren(rows, root, 1, sourceById, mediaByPair, groupChildrenLocal, search, showMediaThumbnail: false, CommentsLayoutMode.Grouped);
            }
        }

        return new(rows, records.Count, truncated);
    }

    private void AppendItem(
        List<ICommentsRowViewModel> rows,
        CommentRecord record,
        int depth,
        Dictionary<string, Source> sourceById,
        Dictionary<(string, string), (Media Media, int SortNumber)> mediaByPair,
        Dictionary<string, List<CommentRecord>> childrenByParent,
        string search,
        bool showMediaThumbnail,
        CommentsLayoutMode layout)
    {
        var ctx = BuildContext(record, depth, sourceById, mediaByPair, childrenByParent, showMediaThumbnail, layout);
        rows.Add(new CommentItemViewModel(record, ctx, this, search));
    }

    private void AppendChildren(
        List<ICommentsRowViewModel> rows,
        CommentRecord parent,
        int depth,
        Dictionary<string, Source> sourceById,
        Dictionary<(string, string), (Media Media, int SortNumber)> mediaByPair,
        Dictionary<string, List<CommentRecord>> childrenByParent,
        string search,
        bool showMediaThumbnail,
        CommentsLayoutMode layout)
    {
        if (!childrenByParent.TryGetValue(parent.Id, out var kids))
        {
            return;
        }

        var sorted = kids.OrderBy(r => r.PublishedAt).ToList();

        if (depth == 1 && sorted.Count > BranchToggleThreshold)
        {
            var isExpanded = _expandedBranches.Contains(parent.Id);
            var visible = isExpanded ? sorted : sorted.Take(VisibleRepliesPerRoot).ToList();

            foreach (var kid in visible)
            {
                AppendItem(rows, kid, depth, sourceById, mediaByPair, childrenByParent, search, showMediaThumbnail, layout);
                AppendChildren(rows, kid, depth + 1, sourceById, mediaByPair, childrenByParent, search, showMediaThumbnail, layout);
            }

            rows.Add(new BranchToggleViewModel(
                parent.Id,
                totalCount: sorted.Count,
                hiddenCount: sorted.Count - VisibleRepliesPerRoot,
                depth,
                isExpanded,
                ToggleBranchAsync));
            return;
        }

        foreach (var kid in sorted)
        {
            AppendItem(rows, kid, depth, sourceById, mediaByPair, childrenByParent, search, showMediaThumbnail, layout);
            AppendChildren(rows, kid, depth + 1, sourceById, mediaByPair, childrenByParent, search, showMediaThumbnail, layout);
        }
    }

    private CommentItemContext BuildContext(
        CommentRecord record,
        int depth,
        Dictionary<string, Source> sourceById,
        Dictionary<(string, string), (Media Media, int SortNumber)> mediaByPair,
        Dictionary<string, List<CommentRecord>> childrenByParent,
        bool showMediaThumbnail,
        CommentsLayoutMode layout)
    {
        var source = sourceById.GetValueOrDefault(record.SourceId);
        var sourceTitle = source?.TitleFull ?? record.SourceId;
        var media = mediaByPair.TryGetValue((record.SourceId, record.ExternalMediaId), out var info) ? info.Media : null;
        var mediaTitle = media?.Title ?? $"<{record.ExternalMediaId}>";

        var hasMutations = source?.Type is ISupportsCommentMutations;
        var hasLikes = source?.Type is ISupportsCommentLikes;
        var hasAuthors = source?.Type is ISupportsCommentAuthors;
        var hasCommentPermalink = source?.Type is ISupportsCommentPermalinks
                                  && !string.IsNullOrEmpty(record.ExternalCommentId)
                                  && !string.IsNullOrEmpty(record.ExternalMediaId);

        var hasMediaExternalLink = HasMediaExternalLink(sourceById, record.SourceId, record.ExternalMediaId);

        var replyCount = CountDescendants(record.Id, childrenByParent);

        var thumbnailUrl = showMediaThumbnail ? ResolveThumbnailUrl(media) : null;

        return new(depth,
            sourceTitle,
            mediaTitle,
            hasCommentPermalink,
            hasMutations,
            hasLikes,
            hasAuthors,
            hasMediaExternalLink,
            replyCount,
            layout,
            thumbnailUrl,
            showMediaThumbnail && depth == 0 && !string.IsNullOrEmpty(thumbnailUrl));
    }

    private static bool HasMediaExternalLink(Dictionary<string, Source> sourceById, string sourceId, string externalMediaId)
    {
        if (string.IsNullOrEmpty(externalMediaId))
        {
            return false;
        }

        if (!sourceById.TryGetValue(sourceId, out var src) || src.Type is null)
        {
            return false;
        }

        try
        {
            return src.Type.GetExternalUri(externalMediaId, src.Settings) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static int CountDescendants(string id, Dictionary<string, List<CommentRecord>> childrenByParent)
    {
        if (!childrenByParent.TryGetValue(id, out var kids))
        {
            return 0;
        }

        var total = kids.Count;

        foreach (var kid in kids)
        {
            total += CountDescendants(kid.Id, childrenByParent);
        }

        return total;
    }

    private static Dictionary<string, List<CommentRecord>> BuildChildrenIndex(IEnumerable<CommentRecord> records, Dictionary<string, CommentRecord> byId)
    {
        var index = new Dictionary<string, List<CommentRecord>>(StringComparer.Ordinal);

        foreach (var r in records)
        {
            if (string.IsNullOrEmpty(r.ParentExternalCommentId))
            {
                continue;
            }

            var parentId = $"{r.SourceId}|{r.ExternalMediaId}|{r.ParentExternalCommentId}";

            if (!byId.ContainsKey(parentId))
            {
                continue;
            }

            if (!index.TryGetValue(parentId, out var bucket))
            {
                bucket = [];
                index[parentId] = bucket;
            }

            bucket.Add(r);
        }

        return index;
    }

    private Dictionary<(string SourceId, string ExternalMediaId), (Media Media, int SortNumber)> BuildMediaIndex()
    {
        var dict = new Dictionary<(string, string), (Media, int)>();

        foreach (var media in _orcestrator.GetMedias())
        {
            foreach (var link in media.Sources)
            {
                if (link is { SourceId: not null, ExternalId: not null })
                {
                    dict[(link.SourceId, link.ExternalId)] = (media, link.SortNumber);
                }
            }
        }

        return dict;
    }

    private static string? ResolveThumbnailUrl(Media? media)
    {
        if (media is null)
        {
            return null;
        }

        foreach (var item in media.Metadata)
        {
            if (item.Key == "PreviewUrl" && !string.IsNullOrEmpty(item.Value))
            {
                return item.Value;
            }
        }

        return null;
    }

    private static string ResolveGroupKey(CommentRecord r, Dictionary<(string, string), (Media Media, int SortNumber)> mediaByPair)
    {
        return mediaByPair.TryGetValue((r.SourceId, r.ExternalMediaId), out var info)
            ? info.Media.Id
            : $"__orphan__|{r.SourceId}|{r.ExternalMediaId}";
    }

    private void ReplaceItems(BuildItemsResult result)
    {
        lock (_itemsLock)
        {
            Items.ReplaceAll(result.Rows);
        }

        TotalCountText = result.TotalCount switch
        {
            0 => "Комментариев: 0",
            _ when result.Truncated => $"Комментариев: ≥{result.TotalCount} (увеличьте лимит)",
            _ => $"Комментариев: {result.TotalCount}",
        };

        TruncatedHint = result.Truncated ? "Увеличьте лимит, чтобы загрузить больше." : null;

        if (result.Rows.Count > 0)
        {
            EmptyIcon = PackIconLucideKind.MessageSquare;
            EmptyHeading = "Комментариев нет";
            EmptyMessage = null;
        }
        else if (!HasSources)
        {
            EmptyIcon = PackIconLucideKind.SlidersHorizontal;
            EmptyHeading = "Источники не настроены";
            EmptyMessage = "Сначала добавьте источники в разделе «Источники и связи».";
        }
        else if (SelectedSourceCount == 0)
        {
            EmptyIcon = PackIconLucideKind.SlidersHorizontal;
            EmptyHeading = "Источники не выбраны";
            EmptyMessage = "Выберите источники в фильтре и нажмите «Загрузить», чтобы получить комментарии.";
        }
        else
        {
            EmptyIcon = PackIconLucideKind.MessageSquare;
            EmptyHeading = "Комментариев нет";
            EmptyMessage = "Под текущие фильтры ничего не подошло. Сбросьте поиск или статус ответа.";
        }
    }

    private Task ToggleBranchAsync(BranchToggleViewModel toggle)
    {
        if (!_expandedBranches.Add(toggle.RootId))
        {
            _expandedBranches.Remove(toggle.RootId);
        }

        ScheduleApplyFilters();
        return Task.CompletedTask;
    }

    private async Task RefreshGroupAsync(MediaGroupHeaderViewModel header)
    {
        var infos = header.Sources.Where(s => s.HasFetchSupport).ToList();

        if (infos.Count == 0)
        {
            return;
        }

        foreach (var info in infos)
        {
            await RefreshSourceMediaAsync(info.SourceId, info.ExternalMediaId);
        }

        ScheduleApplyFilters();
    }

    private async Task RefreshOneSourceAsync(MediaGroupSourceActionViewModel action)
    {
        await RefreshSourceMediaAsync(action.SourceId, action.ExternalMediaId);
        ScheduleApplyFilters();
    }

    private async Task RefreshSourceMediaAsync(string sourceId, string externalMediaId)
    {
        var sources = _orcestrator.GetSources().ToDictionary(s => s.Id);

        if (!sources.TryGetValue(sourceId, out var source) || source.Type is not ISupportsComments)
        {
            return;
        }

        var media = _orcestrator.GetMedias()
            .FirstOrDefault(m => m.Sources.Any(l => l.SourceId == sourceId && l.ExternalId == externalMediaId));
        var link = media?.Sources.FirstOrDefault(l => l.SourceId == sourceId && l.ExternalId == externalMediaId);

        if (media is null || link is null)
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        var action = _actionHolder.Register(
            $"Комментарии: «{media.Title}»",
            "Загрузка…",
            1,
            cts,
            kind: ActionKind.Comments);

        try
        {
            var progress = new Progress<string>(text => action.Status = text);
            var count = await _commentsService.RefreshAsync(source, media, link, progress, cts.Token);
            action.Status = $"{source.TitleFull}: {count}";
            action.ProgressPlus();
            action.Finish();
        }
        catch (OperationCanceledException)
        {
            action.MarkCancelled("Отменено");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось обновить комментарии для «{Title}» в {Source}", media.Title, source.TitleFull);
            action.Fail("Ошибка: " + ex.Message, ex);
        }
    }

    private Task OpenOneSourceAsync(MediaGroupSourceActionViewModel action)
    {
        var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == action.SourceId);

        if (source?.Type is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            var uri = source.Type.GetExternalUri(action.ExternalMediaId, source.Settings);

            if (uri is null)
            {
                return Task.CompletedTask;
            }

            if (!_shellLauncher.Open(uri.ToString()))
            {
                _logger.LogError("Не удалось открыть медиа в источнике {Source}", source.TitleFull);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось открыть медиа в источнике {Source}", source.TitleFull);
        }

        return Task.CompletedTask;
    }

    private async Task FetchFromSourcesAsync(IReadOnlyList<Source> selectedSources)
    {
        if (IsFetching)
        {
            return;
        }

        var supported = selectedSources.Where(s => s.Type is ISupportsComments).ToList();
        var unsupported = selectedSources.Where(s => s.Type is not ISupportsComments).Select(s => s.TitleFull).ToList();

        if (unsupported.Count > 0)
        {
            _dialogs.Info("Невозможно загрузить", $"Источники не поддерживают комментарии: {string.Join(", ", unsupported)}.");
        }

        if (supported.Count == 0)
        {
            return;
        }

        var sinceDays = _settings.FetchSinceDays > 0 ? (int?)_settings.FetchSinceDays : null;
        var since = sinceDays is not null ? DateTime.UtcNow.AddDays(-sinceDays.Value) : (DateTime?)null;
        var takeRecent = _settings.FetchOnlyRecent > 0 ? (int?)_settings.FetchOnlyRecent : null;

        var plans = supported
            .Select(src => (Source: src, Targets: BuildTargetsForSource(src, since, takeRecent)))
            .Where(p => p.Targets.Count > 0)
            .ToList();

        var total = plans.Sum(p => p.Targets.Count);

        if (total == 0)
        {
            _dialogs.Info("Нечего загружать", "В выбранных источниках нет медиа для загрузки.");
            return;
        }

        IsFetching = true;
        _fetchCts = new();
        var token = _fetchCts.Token;

        var sourcesLabel = plans.Count == 1
            ? $"«{plans[0].Source.TitleFull}»"
            : $"{plans.Count} ист.";

        FetchProgressMax = total;
        FetchProgressValue = 0;
        FetchProgressIsIndeterminate = false;
        FetchProgressMessage = $"Загрузка комментариев из {sourcesLabel}…";
        FetchCounterText = $"0 / {total}";

        var rootAction = _actionHolder.Register(
            $"Загрузка комментариев: {sourcesLabel}",
            "Запущена",
            total,
            _fetchCts,
            kind: ActionKind.Comments);

        var counters = new FetchCounters();
        var fetchSw = Stopwatch.StartNew();

        _logger.LogInformation(
            "Старт загрузки комментариев: источников {Sources}, медиа {Total}, since={Since}, takeRecent={TakeRecent}",
            plans.Count,
            total,
            since,
            takeRecent);

        try
        {
            await Task.WhenAll(plans.Select(plan => RunSourceFetchAsync(plan, rootAction, counters, total, token)));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            fetchSw.Stop();

            var ok = Volatile.Read(ref counters.Ok);
            var failed = Volatile.Read(ref counters.Failed);

            var summary = failed > 0
                ? $"Успешно {ok} из {total}, ошибок {failed}"
                : $"Успешно {ok} из {total}";

            if (token.IsCancellationRequested)
            {
                rootAction.MarkCancelled(summary);
                _logger.LogInformation(
                    "Загрузка комментариев отменена за {Elapsed} мс: ok {Ok}, failed {Failed}, всего {Total}",
                    fetchSw.ElapsedMilliseconds,
                    ok,
                    failed,
                    total);
            }
            else if (failed > 0 && ok == 0)
            {
                rootAction.Fail(summary);
                _logger.LogWarning(
                    "Загрузка комментариев провалилась за {Elapsed} мс: ok 0, failed {Failed} из {Total}",
                    fetchSw.ElapsedMilliseconds,
                    failed,
                    total);
            }
            else
            {
                rootAction.Finish(summary);
                _logger.LogInformation(
                    "Загрузка комментариев завершена за {Elapsed} мс: ok {Ok}, failed {Failed} из {Total}",
                    fetchSw.ElapsedMilliseconds,
                    ok,
                    failed,
                    total);
            }

            IsFetching = false;
            FetchProgressMessage = null;
            FetchCounterText = null;
            FetchProgressValue = 0;
            FetchProgressMax = 0;

            _fetchCts?.Dispose();
            _fetchCts = null;

            ScheduleApplyFilters();
        }
    }

    private async Task RunSourceFetchAsync(
        (Source Source, List<(Media Media, MediaSourceLink Link)> Targets) plan,
        ActionHolder.RunningAction rootAction,
        FetchCounters counters,
        int total,
        CancellationToken rootToken)
    {
        var source = plan.Source;
        var targets = plan.Targets;

        using var childCts = CancellationTokenSource.CreateLinkedTokenSource(rootToken);
        var sourceAction = _actionHolder.Register(
            $"Источник: «{source.TitleFull}»",
            $"0 / {targets.Count}",
            targets.Count,
            childCts,
            kind: ActionKind.Comments,
            parent: rootAction);

        var srcOk = 0;
        var srcFailed = 0;

        try
        {
            var maxParallelism = _settings.FetchMaxParallelism <= 0
                ? CommentsViewSettings.DefaultFetchMaxParallelism
                : Math.Clamp(_settings.FetchMaxParallelism, CommentsViewSettings.MinFetchMaxParallelism, CommentsViewSettings.MaxFetchMaxParallelism);

            await Parallel.ForEachAsync(targets, new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = childCts.Token,
            }, async (item, ct) =>
            {
                var (media, link) = item;

                try
                {
                    await _commentsService.RefreshAsync(source, media, link, null, ct);
                    Interlocked.Increment(ref counters.Ok);
                    Interlocked.Increment(ref srcOk);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref counters.Failed);
                    Interlocked.Increment(ref srcFailed);
                    _logger.LogError(ex, "Не удалось загрузить комментарии для «{Title}»", media.Title);
                }

                rootAction.ProgressPlus();
                sourceAction.ProgressPlus();

                var processed = Interlocked.Increment(ref counters.Processed);
                var ok = Volatile.Read(ref counters.Ok);
                var failed = Volatile.Read(ref counters.Failed);

                rootAction.Status = $"{source.TitleFull}: {media.Title}";
                sourceAction.Status = media.Title;

                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    FetchProgressValue = Math.Min(processed, FetchProgressMax);
                    FetchProgressMessage = $"[{processed}/{total}] {source.TitleFull}: «{media.Title}»";
                    FetchCounterText = failed > 0 ? $"✓ {ok}  ✗ {failed}  /  {total}" : $"✓ {ok}  /  {total}";
                });
            });
        }
        catch (OperationCanceledException) when (childCts.IsCancellationRequested)
        {
        }
        finally
        {
            var okFinal = Volatile.Read(ref srcOk);
            var failedFinal = Volatile.Read(ref srcFailed);

            var srcSummary = $"«{source.TitleFull}»: успешно {okFinal} из {targets.Count}";

            if (failedFinal > 0)
            {
                srcSummary += $", ошибок {failedFinal}";
            }

            if (childCts.IsCancellationRequested)
            {
                sourceAction.MarkCancelled(srcSummary);
            }
            else if (failedFinal > 0)
            {
                sourceAction.Fail(srcSummary);
            }
            else
            {
                sourceAction.Finish(srcSummary);
            }
        }
    }

    private sealed class FetchCounters
    {
        public int Ok;
        public int Failed;
        public int Processed;
    }

    private List<(Media Media, MediaSourceLink Link)> BuildTargetsForSource(Source source, DateTime? since, int? takeRecent)
    {
        var all = _orcestrator.GetMedias()
            .SelectMany(m => m.Sources
                .Where(l => l.SourceId == source.Id && !string.IsNullOrEmpty(l.ExternalId))
                .Select(l => (Media: m, Link: l)))
            .OrderByDescending(item => item.Link.SortNumber)
            .ToList();

        IEnumerable<(Media Media, MediaSourceLink Link)> filtered = all;

        if (since is not null)
        {
            filtered = filtered.Where(t => TryGetCreationDate(t.Media) is { } d && d >= since.Value);
        }

        if (takeRecent is not null)
        {
            filtered = filtered.OrderByDescending(t => t.Link.SortNumber).Take(takeRecent.Value);
        }

        return filtered.ToList();
    }

    private static DateTime? TryGetCreationDate(Media media)
    {
        var raw = media.Metadata.FirstOrDefault(m => m.Key == "CreationDate")?.Value;

        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return DateTime.TryParse(raw,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    public Task ExecuteLikeAsync(CommentItemViewModel item, bool liked)
    {
        return RunMutationAsync(item,
            liked ? CommentMutationKind.Like : CommentMutationKind.Unlike,
            async (source, link, ct) =>
            {
                var newCount = await _commentsService.LikeCommentAsync(source, link, item.Record.ExternalCommentId, liked, ct);
                Application.Current?.Dispatcher.Invoke(() => item.ApplyLikeUpdate(liked, newCount));
            });
    }

    public async Task<string?> ExecuteEditAsync(CommentItemViewModel item, string newText)
    {
        string? finalText = null;

        await RunMutationAsync(item, CommentMutationKind.Edit, async (source, link, ct) =>
        {
            var updated = await _commentsService.EditCommentAsync(source, link, item.Record.ExternalCommentId, newText, ct);
            finalText = updated.Text;
        });

        return finalText;
    }

    public Task ExecuteDeleteAsync(CommentItemViewModel item)
    {
        return RunMutationAsync(item, CommentMutationKind.Delete, async (source, link, ct) =>
        {
            await _commentsService.DeleteCommentAsync(source, link, item.Record.ExternalCommentId, ct);
        });
    }

    public Task ExecuteRestoreAsync(CommentItemViewModel item)
    {
        return RunMutationAsync(item, CommentMutationKind.Restore, async (source, link, ct) =>
        {
            await _commentsService.RestoreCommentAsync(source, link, item.Record.ExternalCommentId, ct);
        });
    }

    public void StartReply(CommentItemViewModel target)
    {
        if (_activeReplyEditor is not null)
        {
            CancelReply(_activeReplyEditor);
        }

        var prefix = BuildReplyPrefix(target.AuthorName);

        var editor = new ReplyEditorViewModel(
            target.Record.SourceId,
            target.Record.ExternalMediaId,
            target.Record.ExternalCommentId,
            target.Depth + 1,
            target.HasAuthors,
            prefix,
            SubmitReplyAsync,
            CancelReply);

        _activeReplyEditor = editor;

        var index = Items.IndexOf(target);

        if (index < 0)
        {
            return;
        }

        var insertAt = index + 1;
        Items.Insert(insertAt, editor);

        if (target.HasAuthors)
        {
            _ = LoadAuthorsAsync(target.Record.SourceId, target.Record.ExternalMediaId, editor);
        }
    }

    public void OpenCommentInSource(CommentItemViewModel item)
    {
        var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == item.Record.SourceId);

        if (source?.Type is not ISupportsCommentPermalinks permalinks)
        {
            return;
        }

        try
        {
            var allComments = _commentsService.GetByMedia(item.Record.SourceId, item.Record.ExternalMediaId);
            var rootId = FindRootCommentId(allComments, item.Record.ExternalCommentId);

            var uri = permalinks.GetCommentExternalUri(item.Record.ExternalMediaId,
                item.Record.ExternalCommentId,
                rootId,
                source.Settings,
                null);

            if (uri is null)
            {
                return;
            }

            if (!_shellLauncher.Open(uri.ToString()))
            {
                _logger.LogError("Не удалось открыть комментарий в источнике");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось открыть комментарий в источнике");
        }
    }

    public void OpenMediaInSource(CommentItemViewModel item)
    {
        var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == item.Record.SourceId);

        if (source?.Type is null)
        {
            return;
        }

        try
        {
            var uri = source.Type.GetExternalUri(item.Record.ExternalMediaId, source.Settings);

            if (uri is null)
            {
                return;
            }

            if (!_shellLauncher.Open(uri.ToString()))
            {
                _logger.LogError("Не удалось открыть медиа в источнике");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось открыть медиа в источнике");
        }
    }

    public void OpenExternalLink(Uri uri)
    {
        if (!_shellLauncher.Open(uri.ToString()))
        {
            _logger.LogError("Не удалось открыть ссылку из комментария {Uri}", uri);
        }
    }

    private async Task SubmitReplyAsync(ReplyEditorViewModel editor)
    {
        var text = (editor.Text ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == editor.SourceId);

        if (source?.Type is not ISupportsCommentMutations)
        {
            throw new InvalidOperationException("Источник не поддерживает создание комментариев.");
        }

        var media = _orcestrator.GetMedias()
            .FirstOrDefault(m => m.Sources.Any(l => l.SourceId == editor.SourceId && l.ExternalId == editor.ExternalMediaId));

        var link = media?.Sources.FirstOrDefault(l => l.SourceId == editor.SourceId && l.ExternalId == editor.ExternalMediaId);

        if (media is null || link is null)
        {
            throw new InvalidOperationException("Медиа не найдено локально.");
        }

        using var cts = new CancellationTokenSource();
        var action = _actionHolder.Register(
            $"Отправка комментария: «{media.Title}»",
            "Запущена",
            1,
            cts,
            kind: ActionKind.Comments);

        try
        {
            await _commentsService.CreateCommentAsync(source, link, editor.ParentExternalCommentId, text, editor.SelectedAuthor?.Id, cts.Token);
            action.ProgressPlus();
            action.Finish();
        }
        catch (OperationCanceledException)
        {
            action.MarkCancelled("Отменено");
            throw;
        }
        catch (Exception ex)
        {
            action.Fail("Ошибка: " + ex.Message, ex);
            throw;
        }

        CancelReply(editor);
        ScheduleApplyFilters();
    }

    private void CancelReply(ReplyEditorViewModel editor)
    {
        if (_activeReplyEditor == editor)
        {
            _activeReplyEditor = null;
        }

        Items.Remove(editor);
    }

    private async Task LoadAuthorsAsync(string sourceId, string externalMediaId, ReplyEditorViewModel editor)
    {
        var key = (sourceId, externalMediaId);

        if (_authorsCache.TryGetValue(key, out var cached))
        {
            editor.ReplaceAuthors(cached);
            return;
        }

        if (!_authorsInFlight.Add(key))
        {
            return;
        }

        editor.IsAuthorsLoading = true;

        try
        {
            var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == sourceId);

            if (source?.Type is not ISupportsCommentAuthors)
            {
                return;
            }

            var link = _orcestrator.GetMedias()
                .SelectMany(m => m.Sources)
                .FirstOrDefault(l => l.SourceId == sourceId && l.ExternalId == externalMediaId);

            if (link is null)
            {
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var raw = await _commentsService.GetAvailableAuthorsAsync(source, link, cts.Token);
            var list = raw.Select(a => new CommentAuthorOptionViewModel(a.Id, a.Name, a.AvatarUrl, a.IsDefault)).ToList();

            _authorsCache[key] = list;
            Application.Current?.Dispatcher.Invoke(() => editor.ReplaceAuthors(list));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить список авторов для {Source}/{Media}", sourceId, externalMediaId);
        }
        finally
        {
            editor.IsAuthorsLoading = false;
            _authorsInFlight.Remove(key);
        }
    }

    private string BuildReplyPrefix(string authorName)
    {
        var template = string.IsNullOrEmpty(_settings.ReplyPrefixTemplate) ? "{name}, " : _settings.ReplyPrefixTemplate;
        return template.Replace("{name}", authorName);
    }

    private static string? FindRootCommentId(IReadOnlyList<CommentRecord> comments, string externalCommentId)
    {
        var byId = new Dictionary<string, CommentRecord>(comments.Count, StringComparer.Ordinal);

        foreach (var c in comments)
        {
            byId.TryAdd(c.ExternalCommentId, c);
        }

        if (!byId.TryGetValue(externalCommentId, out var current))
        {
            return null;
        }

        if (string.IsNullOrEmpty(current.ParentExternalCommentId))
        {
            return null;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var rootId = current.ParentExternalCommentId;

        while (byId.TryGetValue(rootId, out var parent)
               && !string.IsNullOrEmpty(parent.ParentExternalCommentId)
               && visited.Add(rootId))
        {
            rootId = parent.ParentExternalCommentId;
        }

        return rootId;
    }

    private async Task RunMutationAsync(CommentItemViewModel item, CommentMutationKind kind, Func<Source, MediaSourceLink, CancellationToken, Task> body)
    {
        var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == item.Record.SourceId);

        if (source is null)
        {
            throw new InvalidOperationException("Источник не найден.");
        }

        var media = _orcestrator.GetMedias()
            .FirstOrDefault(m => m.Sources.Any(l => l.SourceId == item.Record.SourceId && l.ExternalId == item.Record.ExternalMediaId));

        var link = media?.Sources.FirstOrDefault(l => l.SourceId == item.Record.SourceId && l.ExternalId == item.Record.ExternalMediaId);

        if (media is null || link is null)
        {
            throw new InvalidOperationException("Медиа не найдено локально.");
        }

        var label = kind switch
        {
            CommentMutationKind.Edit => "Изменение комментария",
            CommentMutationKind.Delete => "Удаление комментария",
            CommentMutationKind.Restore => "Восстановление комментария",
            CommentMutationKind.Like => "Лайк комментария",
            CommentMutationKind.Unlike => "Снятие лайка",
            _ => "Комментарий",
        };

        using var cts = new CancellationTokenSource();
        var action = _actionHolder.Register(
            $"{label}: «{media.Title}»",
            "Запущена",
            1,
            cts,
            kind: ActionKind.Comments);

        try
        {
            await body(source, link, cts.Token);
            action.Status = $"{source.TitleFull}: ок";
            action.ProgressPlus();
            action.Finish();
        }
        catch (OperationCanceledException)
        {
            action.MarkCancelled("Отменено");
            throw;
        }
        catch (Exception ex)
        {
            action.Fail("Ошибка: " + ex.Message, ex);
            throw;
        }

        if (kind is CommentMutationKind.Delete or CommentMutationKind.Restore)
        {
            ScheduleApplyFilters();
        }
    }

    private enum CommentMutationKind
    {
        None = 0,
        Edit = 1,
        Delete = 2,
        Restore = 3,
        Like = 4,
        Unlike = 5,
    }

    private sealed record BuildItemsResult(List<ICommentsRowViewModel> Rows, int TotalCount, bool Truncated);
}
