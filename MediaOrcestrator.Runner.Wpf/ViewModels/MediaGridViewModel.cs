using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.MediaContextMenu;
using MediaOrcestrator.Runner.Wpf.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Media;

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

    private readonly System.Windows.Threading.DispatcherTimer _selectionDebounceTimer;
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
        Func<IReadOnlyList<Source>> allSourcesProvider)
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

        _selectionDebounceTimer = new(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = SelectionDebounce,
        };

        _selectionDebounceTimer.Tick += OnSelectionDebounceTick;

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

    public RangeObservableCollection<MediaRowViewModel> Rows { get; }

    public ICollectionView RowsView { get; }

    public string? ActiveSortKey { get; private set; }

    public ListSortDirection ActiveSortDirection { get; private set; } = ListSortDirection.Ascending;

    public RangeObservableCollection<Source> ColumnSources { get; }

    public RangeObservableCollection<MetadataColumnInfo> MetadataColumns { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<SeriesSourceCoverage>> SeriesCoverage { get; private set; } = EmptyCoverage;

    public event EventHandler? ColumnsChanged;

    public event EventHandler? AutoFitColumnsRequested;

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

    public void BuildContextMenu(MediaRowViewModel? triggerRow, Source? specificSource)
    {
        if (triggerRow is not null && !triggerRow.IsSelected)
        {
            foreach (var row in Rows)
            {
                row.IsSelected = false;
            }

            triggerRow.IsSelected = true;
        }

        var selected = Rows.Where(r => r.IsSelected).Select(r => r.Media).ToList();

        if (selected.Count == 0 && triggerRow is not null)
        {
            selected.Add(triggerRow.Media);
        }

        ContextMenuItems = BuildMenu(selected, specificSource, includeAsync: true);
        OnPropertyChanged(nameof(ContextMenuItems));
    }

    private void BuildPanelActions()
    {
        var selected = Rows.Where(r => r.IsSelected).Select(r => r.Media).ToList();

        PanelActionItems = BuildMenu(selected, null, includeAsync: false);

        var primary = PanelActionItems
            .Where(i => !i.IsSeparator && (i.HasChildren || i.Command is not null) && i.Icon != PackIconLucideKind.Trash2)
            .ToList();

        PrimaryActionGroups = BuildActionGroups(primary);
        TransferMenuItems = BuildTransferMenu();

        OnPropertyChanged(nameof(PanelActionItems));
        OnPropertyChanged(nameof(HasPanelActions));
        OnPropertyChanged(nameof(PrimaryActionGroups));
        OnPropertyChanged(nameof(HasPrimaryActions));
        OnPropertyChanged(nameof(TransferMenuItems));
    }

    private ObservableCollection<MediaMenuItemViewModel> BuildTransferMenu()
    {
        var items = new ObservableCollection<MediaMenuItemViewModel>();

        foreach (var item in PanelActionItems.Where(i => i.IsTransferAction))
        {
            items.Add(new()
            {
                Text = item.Text.StartsWith(SyncMenuTextPrefix, StringComparison.Ordinal)
                    ? item.Text[SyncMenuTextPrefix.Length..]
                    : item.Text,
                Icon = item.Icon,
                GlyphBrush = item.GlyphBrush,
                Tooltip = item.Tooltip,
                IsEnabled = item.IsEnabled,
                Command = item.Command,
            });
        }

        if (items.Count > 0)
        {
            items.Add(MediaMenuItemViewModel.Separator());
        }

        items.Add(new()
        {
            Text = "Создать связь",
            Icon = PackIconLucideKind.Plus,
            Tooltip = "Открыть раздел «Источники и связи»",
            Command = CreateRelationCommand,
        });

        return items;
    }

    private static IReadOnlyList<PrimaryActionGroup> BuildActionGroups(IReadOnlyList<MediaMenuItemViewModel> topLevel)
    {
        var groups = new List<PrimaryActionGroup>();

        foreach (var byIcon in topLevel.GroupBy(i => i.IconOrDefault))
        {
            var members = byIcon.ToList();
            var choices = new List<MediaMenuItemViewModel>();

            foreach (var member in members)
            {
                if (member is { HasChildren: true, Children: not null })
                {
                    choices.AddRange(member.Children.Where(c => !c.IsSeparator));
                }
                else
                {
                    choices.Add(member);
                }
            }

            if (choices.Count == 0)
            {
                continue;
            }

            groups.Add(new PrimaryActionGroup
            {
                Icon = byIcon.Key,
                Brush = members[0].GlyphBrush,
                Label = string.Join(" · ", members.Select(m => m.Text)),
                Choices = choices,
            });
        }

        return groups;
    }

    private ObservableCollection<MediaMenuItemViewModel> BuildMenu(List<Media> selected, Source? specificSource, bool includeAsync)
    {
        if (selected.Count == 0)
        {
            return [];
        }

        var selection = new MediaSelection(selected, specificSource)
        {
            InSelectionOrder = OrderBySelection(selected),
            GridSources = ColumnSources.ToList(),
        };

        var ctx = _actionContextFactory.Create(_actionUi);

        return _contextMenuBuilder.Build(selection, ctx, includeAsync);
    }

    private IReadOnlyList<Media> OrderBySelection(IReadOnlyList<Media> selected)
    {
        if (selected.Count < 2)
        {
            return selected;
        }

        var rank = new Dictionary<Media, int>(_selectionOrder.Count);

        for (var i = 0; i < _selectionOrder.Count; i++)
        {
            rank[_selectionOrder[i].Media] = i;
        }

        return selected
            .OrderBy(m => rank.TryGetValue(m, out var r) ? r : int.MaxValue)
            .ToList();
    }

    [RelayCommand]
    private void OpenInspector(MediaRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        foreach (var other in Rows)
        {
            other.IsSelected = ReferenceEquals(other, row);
        }

        RequestExpandInspector?.Invoke(this, EventArgs.Empty);

        UpdateSelectionState();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = false;
        }

        UpdateSelectionState();
    }

    [RelayCommand]
    private void CreateRelation()
    {
        _actionContextFactory.Create(_actionUi).Navigation.OpenSourcesGraph();
    }

    [RelayCommand]
    private void OpenRenameOperation() => OpenOperation(OperationsTab.Rename);

    [RelayCommand]
    private void OpenPreviewOperation() => OpenOperation(OperationsTab.Preview);

    [RelayCommand]
    private void OpenMergeOperation() => OpenOperation(OperationsTab.Merge);

    private void OpenOperation(OperationsTab tab)
    {
        var selected = Rows.Where(r => r.IsSelected).Select(r => r.Media).ToList();

        if (selected.Count == 0)
        {
            return;
        }

        _actionContextFactory.Create(_actionUi).Navigation.OpenOperations(tab, OrderBySelection(selected));
    }

    [RelayCommand]
    private void ToggleAllRowsSelection(bool value)
    {
        foreach (var row in Rows)
        {
            row.IsSelected = value;
        }
    }

    public event EventHandler? RequestExpandInspector;

    partial void OnIsCompactChanged(bool value)
    {
        foreach (var row in Rows)
        {
            row.IsCompact = value;
        }

        if (!_suppressPrefPersist)
        {
            _settings.SetValue(CompactSettingKey, value ? "true" : "false");
        }
    }

    partial void OnIsSourceHeaderCompactChanged(bool value)
    {
        if (!_suppressPrefPersist)
        {
            _settings.SetValue(SourceHeaderCompactSettingKey, value ? "true" : "false");
        }

        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSourceHeaderCompactLengthChanged(int value)
    {
        if (!_suppressPrefPersist)
        {
            _settings.SetValue(SourceHeaderCompactLengthSettingKey, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (IsSourceHeaderCompact)
        {
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDecreaseSourceHeaderLength))]
    private void DecreaseSourceHeaderLength()
    {
        SourceHeaderCompactLength = Math.Max(MinSourceHeaderCompactLength, SourceHeaderCompactLength - 1);
    }

    private bool CanDecreaseSourceHeaderLength() => SourceHeaderCompactLength > MinSourceHeaderCompactLength;

    [RelayCommand(CanExecute = nameof(CanIncreaseSourceHeaderLength))]
    private void IncreaseSourceHeaderLength()
    {
        SourceHeaderCompactLength = Math.Min(MaxSourceHeaderCompactLength, SourceHeaderCompactLength + 1);
    }

    private bool CanIncreaseSourceHeaderLength() => SourceHeaderCompactLength < MaxSourceHeaderCompactLength;

    [RelayCommand]
    private void AutoFitColumns()
    {
        AutoFitColumnsRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsGroupedChanged(bool value)
    {
        if (!_suppressPrefPersist)
        {
            _settings.SetValue(GroupedSettingKey, value ? "true" : "false");
        }

        ApplyGrouping();
    }

    private void ApplyGrouping()
    {
        if (RowsView is not ListCollectionView list)
        {
            return;
        }

        using (list.DeferRefresh())
        {
            if (IsGrouped)
            {
                BuildSeriesCoverage();

                if (list.GroupDescriptions.Count == 0)
                {
                    list.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MediaRowViewModel.SeriesName)));
                }
            }
            else
            {
                list.GroupDescriptions.Clear();
                ClearSeriesCoverage();
            }

            ApplySortCore();
        }

        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private static int CompareSeries(MediaRowViewModel a, MediaRowViewModel b)
    {
        if (a.IsNoSeries != b.IsNoSeries)
        {
            return a.IsNoSeries ? 1 : -1;
        }

        return NaturalStringComparer.Instance.Compare(a.SeriesName, b.SeriesName);
    }

    private static int CompareEpisodeThenTitle(MediaRowViewModel a, MediaRowViewModel b)
    {
        var hasA = a.EpisodeNumber.HasValue;
        var hasB = b.EpisodeNumber.HasValue;

        if (hasA != hasB)
        {
            return hasA ? -1 : 1;
        }

        if (hasA && a.EpisodeNumber!.Value != b.EpisodeNumber!.Value)
        {
            return a.EpisodeNumber.Value.CompareTo(b.EpisodeNumber.Value);
        }

        return NaturalStringComparer.Instance.Compare(a.Title, b.Title);
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

    private void OnSelectionDebounceTick(object? sender, EventArgs e)
    {
        _selectionDebounceTimer.Stop();
        UpdateSelectionState();
    }

    private bool OnFilterRow(object? value)
    {
        return value is MediaRowViewModel row && _filter.Matches(row);
    }

    private void UpdateSelectionState()
    {
        var selected = Rows.Where(r => r.IsSelected).ToList();
        SelectedCount = selected.Count;

        if (selected.Count == 1)
        {
            Focused = selected[0];
            _inspector.ShowSingle(selected[0].Media, _allSourcesProvider());
            MultiSourceTallies.Clear();
            MultiStatusBreakdown.Clear();
        }
        else if (selected.Count >= 2)
        {
            Focused = null;
            _inspector.ShowMulti(selected.Count);
            BuildMultiAggregates(selected);
        }
        else
        {
            Focused = null;
            _inspector.Clear();
            MultiSourceTallies.Clear();
            MultiStatusBreakdown.Clear();
            MultiSummaryText = string.Empty;
        }

        OnPropertyChanged(nameof(IsInspectorOpen));
        BuildPanelActions();
    }

    private void BuildMultiAggregates(IReadOnlyList<MediaRowViewModel> selected)
    {
        var sourceTallies = new List<MultiSourceTally>(ColumnSources.Count);

        for (var i = 0; i < ColumnSources.Count; i++)
        {
            var present = 0;
            var errors = 0;

            foreach (var row in selected)
            {
                if (i >= row.Cells.Count)
                {
                    continue;
                }

                var cell = row.Cells[i];

                if (!cell.HasLink)
                {
                    continue;
                }

                present++;

                if (cell.Status?.Id == MediaStatus.Error)
                {
                    errors++;
                }
            }

            sourceTallies.Add(new(ColumnSources[i].Title, present, selected.Count, errors));
        }

        MultiSourceTallies.ReplaceAll(sourceTallies);

        var allCells = selected.SelectMany(r => r.Cells).ToList();

        var breakdown = allCells
            .Where(c => c.HasLink)
            .GroupBy(c => c.Status?.Id ?? "?")
            .Select(g =>
            {
                var first = g.First();

                return new MediaStatusTally(first.Status?.Id ?? string.Empty, first.Brush, g.Count(),
                    first.Status?.Text ?? "–", MediaStatusTally.OrderOf(first.Status?.Id));
            })
            .OrderBy(t => t.Order)
            .ToList();

        var missing = allCells.Count(c => !c.HasLink);

        if (missing > 0)
        {
            breakdown.Add(MediaStatusTally.Missing(missing));
        }

        MultiStatusBreakdown.ReplaceAll(breakdown);

        var fully = selected.Count(r => r.IsFullySynced);
        var withErrors = selected.Count(r => r.ErrorCount > 0);

        MultiSummaryText = withErrors > 0
            ? $"Полностью синхронизированы: {fully} из {selected.Count} · с ошибками: {withErrors}"
            : $"Полностью синхронизированы: {fully} из {selected.Count}";
    }

    [RelayCommand]
    private void FocusStatusFilter(string? statusId)
    {
        if (string.IsNullOrEmpty(statusId))
        {
            return;
        }

        _filter.FocusStatus(statusId);
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
            newRows.Add(new(_allMedias[i], columns, metaColumns, seriesEntries[i])
            {
                IsCompact = IsCompact,
            });
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

    public MediaRebuildBreakdown ApplyDomainData(List<Media> medias)
    {
        _allMedias.Clear();
        _allMedias.AddRange(medias);

        return RebuildColumnsAndRows();
    }

    public Action<IReadOnlyList<PageRenderStage>, long>? PublishMetric { get; set; }

    private static string FormatMs(long ms) => $"{ms} мс";

    public sealed record MediaRebuildBreakdown(long ColumnsMs, long RowsMs, long ViewMs);

    public static string MetadataColumnKey(string columnId) => MetadataColumnKeyPrefix + columnId;

    public static string SourceColumnKey(string sourceId) => SourceColumnKeyPrefix + sourceId;

    public void ApplySort(string key, ListSortDirection direction)
    {
        ActiveSortKey = key;
        ActiveSortDirection = direction;
        ApplySortCore();

        if (!_suppressPrefPersist)
        {
            _settings.SetValue(SortSettingKey, $"{key}|{direction}");
        }
    }

    private void ReapplySort()
    {
        if (ActiveSortKey is not null || IsGrouped)
        {
            ApplySortCore();
        }
    }

    private void ApplySortCore()
    {
        if (RowsView is not ListCollectionView list)
        {
            return;
        }

        var inner = BuildColumnComparer();

        list.CustomSort = IsGrouped ? BuildGroupedComparer(inner) : inner;
    }

    private Comparer<MediaRowViewModel>? BuildColumnComparer()
    {
        if (ActiveSortKey is null)
        {
            return null;
        }

        var sign = ActiveSortDirection == ListSortDirection.Ascending ? 1 : -1;

        if (ActiveSortKey == TitleColumnKey)
        {
            return CreateTitleComparer(sign);
        }

        if (ActiveSortKey.StartsWith(MetadataColumnKeyPrefix, StringComparison.Ordinal))
        {
            var index = IndexOfMetadataColumn(ActiveSortKey[MetadataColumnKeyPrefix.Length..]);
            return index >= 0 ? CreateMetadataComparer(index, sign) : null;
        }

        if (ActiveSortKey.StartsWith(SourceColumnKeyPrefix, StringComparison.Ordinal))
        {
            var index = IndexOfSourceColumn(ActiveSortKey[SourceColumnKeyPrefix.Length..]);
            return index >= 0 ? CreateSourceComparer(index, sign) : null;
        }

        return null;
    }

    private static Comparer<MediaRowViewModel> BuildGroupedComparer(Comparer<MediaRowViewModel>? inner) =>
        Comparer<MediaRowViewModel>.Create((a, b) =>
        {
            var bySeries = CompareSeries(a, b);

            if (bySeries != 0)
            {
                return bySeries;
            }

            return inner?.Compare(a, b) ?? CompareEpisodeThenTitle(a, b);
        });

    private int IndexOfMetadataColumn(string columnId)
    {
        for (var i = 0; i < MetadataColumns.Count; i++)
        {
            if (string.Equals(MetadataColumns[i].ColumnId, columnId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private int IndexOfSourceColumn(string sourceId)
    {
        for (var i = 0; i < ColumnSources.Count; i++)
        {
            if (string.Equals(ColumnSources[i].Id, sourceId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static Comparer<MediaRowViewModel> CreateTitleComparer(int sign) =>
        Comparer<MediaRowViewModel>.Create((a, b) =>
            NaturalStringComparer.Instance.Compare(a.Title, b.Title) * sign);

    private static Comparer<MediaRowViewModel> CreateMetadataComparer(int columnIndex, int sign) =>
        Comparer<MediaRowViewModel>.Create((a, b) =>
        {
            var valueA = columnIndex < a.MetadataCells.Count ? a.MetadataCells[columnIndex] : string.Empty;
            var valueB = columnIndex < b.MetadataCells.Count ? b.MetadataCells[columnIndex] : string.Empty;

            var hasA = !string.IsNullOrEmpty(valueA);
            var hasB = !string.IsNullOrEmpty(valueB);

            if (hasA != hasB)
            {
                return hasA ? -1 : 1;
            }

            var primary = NaturalStringComparer.Instance.Compare(valueA, valueB) * sign;

            return primary != 0
                ? primary
                : NaturalStringComparer.Instance.Compare(a.Title, b.Title);
        });

    private static Comparer<MediaRowViewModel> CreateSourceComparer(int columnIndex, int sign) =>
        Comparer<MediaRowViewModel>.Create((a, b) =>
        {
            var cellA = columnIndex < a.Cells.Count ? a.Cells[columnIndex] : null;
            var cellB = columnIndex < b.Cells.Count ? b.Cells[columnIndex] : null;

            var hasA = cellA?.HasLink == true;
            var hasB = cellB?.HasLink == true;

            if (hasA != hasB)
            {
                return hasA ? -1 : 1;
            }

            if (!hasA)
            {
                return NaturalStringComparer.Instance.Compare(a.Title, b.Title);
            }

            var primary = cellA!.SourceSortNumber.CompareTo(cellB!.SourceSortNumber) * sign;

            if (primary != 0)
            {
                return primary;
            }

            var secondary = cellA.SortKey.CompareTo(cellB.SortKey);

            return secondary != 0
                ? secondary
                : NaturalStringComparer.Instance.Compare(a.Title, b.Title);
        });

    public bool TryGetColumnWidth(string key, out double width) => _columnWidths.TryGetValue(key, out width);

    public void SetColumnWidth(string key, double width)
    {
        if (double.IsNaN(width) || width < MinPersistedColumnWidth || width > MaxPersistedColumnWidth)
        {
            return;
        }

        if (_columnWidths.TryGetValue(key, out var existing) && Math.Abs(existing - width) < 0.5)
        {
            return;
        }

        _columnWidths[key] = width;

        if (!_suppressPrefPersist)
        {
            PersistColumnWidths();
        }
    }

    public void ClearColumnWidth(string key)
    {
        if (_columnWidths.Remove(key) && !_suppressPrefPersist)
        {
            PersistColumnWidths();
        }
    }

    private void PersistColumnWidths()
    {
        var text = string.Join(";", _columnWidths
            .Select(kv => $"{kv.Key}={kv.Value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}"));

        _settings.SetValue(ColumnWidthsSettingKey, text);
    }

    private void LoadViewPreferences()
    {
        _suppressPrefPersist = true;

        try
        {
            if (bool.TryParse(_settings.GetStringValue(CompactSettingKey), out var compact))
            {
                IsCompact = compact;
            }

            if (bool.TryParse(_settings.GetStringValue(SourceHeaderCompactSettingKey), out var compactHeaders))
            {
                IsSourceHeaderCompact = compactHeaders;
            }

            if (int.TryParse(_settings.GetStringValue(SourceHeaderCompactLengthSettingKey),
                    System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var headerLength))
            {
                SourceHeaderCompactLength = Math.Clamp(headerLength, MinSourceHeaderCompactLength, MaxSourceHeaderCompactLength);
            }

            if (bool.TryParse(_settings.GetStringValue(GroupedSettingKey), out var grouped))
            {
                IsGrouped = grouped;
            }

            LoadSort();
            LoadColumnWidths();
        }
        finally
        {
            _suppressPrefPersist = false;
        }
    }

    private void LoadSort()
    {
        var raw = _settings.GetStringValue(SortSettingKey);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var sep = raw.LastIndexOf('|');

        if (sep > 0 && Enum.TryParse<ListSortDirection>(raw[(sep + 1)..], out var direction))
        {
            ActiveSortKey = raw[..sep];
            ActiveSortDirection = direction;
        }
    }

    private void LoadColumnWidths()
    {
        var raw = _settings.GetStringValue(ColumnWidthsSettingKey);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        foreach (var entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = entry.LastIndexOf('=');

            if (eq <= 0)
            {
                continue;
            }

            if (double.TryParse(entry[(eq + 1)..], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var width)
                && width >= MinPersistedColumnWidth
                && width <= MaxPersistedColumnWidth)
            {
                _columnWidths[entry[..eq]] = width;
            }
        }
    }

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
}
