using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.MediaContextMenu;
using MediaOrcestrator.Runner.Wpf.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MediaGridViewModel : ObservableObject
{
    private const string SortSettingKey = "wpf.media.sort";
    private const string ColumnWidthsSettingKey = "wpf.media.column_widths";
    private const string CompactSettingKey = "wpf.media.compact";
    private const string SourceHeaderCompactSettingKey = "wpf.media.compact_source_headers";
    private const string SourceHeaderCompactLengthSettingKey = "wpf.media.compact_source_headers.length";
    private const string GroupedSettingKey = "wpf.media.grouped";

    private const int MinSourceHeaderCompactLength = 1;
    private const int MaxSourceHeaderCompactLength = 6;
    private const int DefaultSourceHeaderCompactLength = 3;

    public const string TitleColumnKey = "title";

    private const string SyncMenuTextPrefix = "Синхронизировать ";
    private const string MetadataColumnKeyPrefix = "meta:";
    private const string SourceColumnKeyPrefix = "source:";
    private const double MinPersistedColumnWidth = 20;
    private const double MaxPersistedColumnWidth = 2000;

    private static readonly TimeSpan SelectionDebounce = TimeSpan.FromMilliseconds(80);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SeriesSourceCoverage>> EmptyCoverage =
        new Dictionary<string, IReadOnlyList<SeriesSourceCoverage>>();

    private readonly MediaFilterViewModel _filter;
    private readonly MediaInspectorViewModel _inspector;
    private readonly MediaActionContextFactory _actionContextFactory;
    private readonly IMediaActionUi _actionUi;
    private readonly ISettingsStore _settings;
    private readonly MediaContextMenuBuilder _contextMenuBuilder;
    private readonly Func<IReadOnlyList<Source>> _allSourcesProvider;

    private readonly IUiTimer _selectionDebounceTimer;
    private readonly List<Media> _allMedias = [];
    private readonly Dictionary<string, double> _columnWidths = new(StringComparer.Ordinal);
    private readonly List<MediaRowViewModel> _selectionOrder = [];

    private bool _suppressPrefPersist;

    public MediaGridViewModel(
        MediaFilterViewModel filter,
        MediaInspectorViewModel inspector,
        MediaActionContextFactory actionContextFactory,
        IMediaActionUi actionUi,
        MediaContextMenuBuilder contextMenuBuilder,
        ISettingsStore settings,
        Func<IReadOnlyList<Source>> allSourcesProvider,
        IUiDispatcher dispatcher)
    {
        _filter = filter;
        _inspector = inspector;
        _actionContextFactory = actionContextFactory;
        _actionUi = actionUi;
        _contextMenuBuilder = contextMenuBuilder;
        _settings = settings;
        _allSourcesProvider = allSourcesProvider;

        Rows = [];
        ColumnSources = [];
        MetadataColumns = [];
        ContextMenuItems = [];
        PanelActionItems = [];

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = OnFilterRow;

        _selectionDebounceTimer = dispatcher.CreateTimer(SelectionDebounce, OnSelectionDebounceTick);

        _filter.FiltersChanged += OnFilterFiltersChanged;
        _filter.ColumnsAffected += OnFilterColumnsAffected;

        LoadViewPreferences();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(IsHelpVisible))]
    [NotifyPropertyChangedFor(nameof(IsSelectionVisible))]
    [NotifyPropertyChangedFor(nameof(SelectedSummaryText))]
    [NotifyPropertyChangedFor(nameof(PanelTitle))]
    private int _selectedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInspectorOpen))]
    [NotifyPropertyChangedFor(nameof(PanelTitle))]
    private MediaRowViewModel? _focused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private string? _loadError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowHeight))]
    [NotifyPropertyChangedFor(nameof(RowMinHeight))]
    [NotifyPropertyChangedFor(nameof(IsViewCustomized))]
    private bool _isCompact;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSourceHeaderLengthAdjustable))]
    [NotifyPropertyChangedFor(nameof(IsViewCustomized))]
    private bool _isSourceHeaderCompact;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DecreaseSourceHeaderLengthCommand))]
    [NotifyCanExecuteChangedFor(nameof(IncreaseSourceHeaderLengthCommand))]
    private int _sourceHeaderCompactLength = DefaultSourceHeaderCompactLength;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewCustomized))]
    private bool _isGrouped;

    [ObservableProperty]
    private string _multiSummaryText = string.Empty;

    public event EventHandler? ColumnsChanged;

    public event EventHandler? AutoFitColumnsRequested;

    public event EventHandler? RequestExpandInspector;

    public RangeObservableCollection<MediaRowViewModel> Rows { get; }

    public ICollectionView RowsView { get; }

    public string? ActiveSortKey { get; private set; }

    public ListSortDirection ActiveSortDirection { get; private set; } = ListSortDirection.Ascending;

    public RangeObservableCollection<Source> ColumnSources { get; }

    public RangeObservableCollection<MetadataColumnInfo> MetadataColumns { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<SeriesSourceCoverage>> SeriesCoverage { get; private set; } = EmptyCoverage;

    public RangeObservableCollection<MultiSourceTally> MultiSourceTallies { get; } = [];

    public RangeObservableCollection<MediaStatusTally> MultiStatusBreakdown { get; } = [];

    public ObservableCollection<MediaMenuItemViewModel> ContextMenuItems { get; private set; }

    public ObservableCollection<MediaMenuItemViewModel> PanelActionItems { get; private set; }

    public ObservableCollection<MediaMenuItemViewModel> TransferMenuItems { get; private set; } = [];

    public bool HasPanelActions => PanelActionItems.Count > 0;

    public IReadOnlyList<PrimaryActionGroup> PrimaryActionGroups { get; private set; } = [];

    public bool HasPrimaryActions => PrimaryActionGroups.Count > 0;

    public int TotalCount => Rows.Count;

    public int VisibleCount => RowsView is ListCollectionView list ? list.Count : Rows.Count;

    public bool HasLoadError => LoadError is not null;

    public bool IsDatabaseEmpty => TotalCount == 0;

    public bool ShowEmptyState => !IsLoading && !HasLoadError && VisibleCount == 0;

    public bool HasSelection => SelectedCount > 0;

    public bool IsInspectorOpen => Focused is not null;

    public double RowHeight => IsCompact ? 28 : double.NaN;

    public double RowMinHeight => IsCompact ? 28 : 38;

    public bool IsHelpVisible => SelectedCount == 0;

    public bool IsSelectionVisible => SelectedCount >= 1;

    public string SelectedSummaryText => SelectedCount switch
    {
        0 => "Ничего не выделено",
        1 => "Выделено 1 медиа",
        _ => $"Выделено {SelectedCount} медиа",
    };

    public string PanelTitle => SelectedCount switch
    {
        0 => "Панель деталей",
        1 => string.IsNullOrEmpty(Focused?.Title) ? "Детали медиа" : Focused!.Title,
        _ => SelectedSummaryText,
    };

    public bool IsSourceHeaderLengthAdjustable => IsSourceHeaderCompact;

    public bool IsViewCustomized => IsCompact || IsGrouped || IsSourceHeaderCompact;

    public Action<IReadOnlyList<PageRenderStage>, long>? PublishMetric { get; set; }

    public Func<Task>? RetryLoadAction { get; set; }

    [RelayCommand]
    private Task RetryLoadAsync() => RetryLoadAction?.Invoke() ?? Task.CompletedTask;

    public void ClearOnLoadError()
    {
        _allMedias.Clear();
        DetachRowEvents();
        Rows.ReplaceAll([]);
        ColumnSources.ReplaceAll([]);
        MetadataColumns.ReplaceAll([]);
        ClearSeriesCoverage();
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
        RowsView.Refresh();
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(IsDatabaseEmpty));
        OnPropertyChanged(nameof(ShowEmptyState));
        UpdateSelectionState();
    }

    internal void RefreshViewForAutomation()
    {
        RowsView.Refresh();
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(IsDatabaseEmpty));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    public static string MetadataColumnKey(string columnId) => MetadataColumnKeyPrefix + columnId;

    public static string SourceColumnKey(string sourceId) => SourceColumnKeyPrefix + sourceId;

    public MediaRebuildBreakdown ApplyDomainData(List<Media> medias)
    {
        _allMedias.Clear();
        _allMedias.AddRange(medias);

        return RebuildColumnsAndRows();
    }

    public MediaRebuildBreakdown RebuildColumnsAndRows()
    {
        var swCols = Stopwatch.StartNew();

        var activeSourceIds = _filter.ActiveSourceIds();
        var activeRelationSourceIds = _filter.ActiveRelationSourceIds();
        var allSources = _allSourcesProvider();

        var columns = allSources
            .Where(s => (activeSourceIds.Count == 0 || activeSourceIds.Contains(s.Id))
                && (activeRelationSourceIds.Count == 0 || activeRelationSourceIds.Contains(s.Id)))
            .ToList();

        ColumnSources.ReplaceAll(columns);

        var metaColumns = _filter.SelectedMetadataColumns();
        MetadataColumns.ReplaceAll(metaColumns);
        swCols.Stop();

        var swRows = Stopwatch.StartNew();
        DetachRowEvents();

        var seriesEntries = MediaSeriesParser.Assign(_allMedias);
        var newRows = new List<MediaRowViewModel>(_allMedias.Count);

        for (var i = 0; i < _allMedias.Count; i++)
        {
            newRows.Add(new(_allMedias[i], columns, metaColumns, seriesEntries[i]));
        }

        Rows.ReplaceAll(newRows);
        AttachRowEvents();

        if (IsGrouped)
        {
            BuildSeriesCoverage();
        }

        ColumnsChanged?.Invoke(this, EventArgs.Empty);
        swRows.Stop();

        ReapplySort();

        var swView = Stopwatch.StartNew();
        RowsView.Refresh();
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(IsDatabaseEmpty));
        OnPropertyChanged(nameof(ShowEmptyState));
        swView.Stop();

        UpdateSelectionState();

        return new(swCols.ElapsedMilliseconds, swRows.ElapsedMilliseconds, swView.ElapsedMilliseconds);
    }

    private void BuildSeriesCoverage()
    {
        var sources = ColumnSources;

        if (sources.Count == 0 || Rows.Count == 0)
        {
            ClearSeriesCoverage();
            return;
        }

        var bySeries = new Dictionary<string, (int Total, int[] Present)>(StringComparer.Ordinal);

        foreach (var row in Rows)
        {
            if (!bySeries.TryGetValue(row.SeriesName, out var acc))
            {
                acc = (0, new int[sources.Count]);
            }

            acc.Total++;

            for (var i = 0; i < sources.Count && i < row.Cells.Count; i++)
            {
                if (row.Cells[i].HasLink)
                {
                    acc.Present[i]++;
                }
            }

            bySeries[row.SeriesName] = acc;
        }

        var map = new Dictionary<string, IReadOnlyList<SeriesSourceCoverage>>(bySeries.Count, StringComparer.Ordinal);

        foreach (var (series, acc) in bySeries)
        {
            var coverage = new List<SeriesSourceCoverage>(sources.Count);

            for (var i = 0; i < sources.Count; i++)
            {
                coverage.Add(new(sources[i].Title, acc.Present[i], acc.Total));
            }

            map[series] = coverage;
        }

        SeriesCoverage = map;
        OnPropertyChanged(nameof(SeriesCoverage));
    }

    private void ClearSeriesCoverage()
    {
        if (ReferenceEquals(SeriesCoverage, EmptyCoverage))
        {
            return;
        }

        SeriesCoverage = EmptyCoverage;
        OnPropertyChanged(nameof(SeriesCoverage));
    }

    private void OnFilterFiltersChanged(object? sender, EventArgs e)
    {
        var sw = Stopwatch.StartNew();
        RowsView.Refresh();
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(ShowEmptyState));
        sw.Stop();

        PublishMetric?.Invoke(
        [
            new("Фильтр", FormatMs(sw.ElapsedMilliseconds)),
        ], sw.ElapsedMilliseconds);
    }

    private void OnFilterColumnsAffected(object? sender, EventArgs e)
    {
        var b = RebuildColumnsAndRows();
        var vmMs = b.ColumnsMs + b.RowsMs + b.ViewMs;

        PublishMetric?.Invoke(
        [
            new("Перестроение колонок", FormatMs(b.ColumnsMs)),
            new("Сборка строк", FormatMs(b.RowsMs)),
            new("Обновление таблицы", FormatMs(b.ViewMs)),
        ], vmMs);
    }

    private void OnSelectionDebounceTick()
    {
        _selectionDebounceTimer.Stop();
        UpdateSelectionState();
    }

    private bool OnFilterRow(object? value) => value is MediaRowViewModel row && _filter.Matches(row);

    private static string FormatMs(long ms) => $"{ms} мс";

    private void DetachRowEvents()
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        _selectionOrder.Clear();
    }

    private void AttachRowEvents()
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MediaRowViewModel.IsSelected) || sender is not MediaRowViewModel row)
        {
            return;
        }

        if (row.IsSelected)
        {
            if (!_selectionOrder.Contains(row))
            {
                _selectionOrder.Add(row);
            }
        }
        else
        {
            _selectionOrder.Remove(row);
        }

        _selectionDebounceTimer.Stop();
        _selectionDebounceTimer.Start();
    }

    public sealed record MediaRebuildBreakdown(long ColumnsMs, long RowsMs, long ViewMs);
}
