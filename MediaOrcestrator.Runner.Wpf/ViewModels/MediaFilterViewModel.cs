using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using System.ComponentModel;
using System.Windows.Data;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MediaFilterViewModel : ObservableObject
{
    private const string SearchSettingKey = "wpf.media.search";
    private const string SourceFilterSettingKey = "wpf.media.sources";
    private const string StatusFilterSettingKey = "wpf.media.status";
    private const string RelationFilterSettingKey = "wpf.media.relations";
    private const string MetadataColumnsSettingKey = "wpf.media.metadata_columns";

    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    private readonly ISettingsStore _settings;
    private readonly IUiTimer _searchDebounceTimer;

    private readonly List<Source> _allSources = [];
    private readonly List<Media> _allMedias = [];
    private readonly List<SourceSyncRelation> _allRelations = [];
    private readonly Dictionary<string, MetadataColumnInfo> _availableMetaById = new(StringComparer.Ordinal);

    private HashSet<string> _activeRelationSourceIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressFilterPersist;

    public MediaFilterViewModel(ISettingsStore settings, IUiDispatcher dispatcher)
    {
        _settings = settings;

        SourceFilters = [];
        StatusFilters = [];
        RelationFilters = [];
        MetadataColumnFilters = [];

        MetadataColumnFiltersView = CollectionViewSource.GetDefaultView(MetadataColumnFilters);
        MetadataColumnFiltersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FilterOptionViewModel.GroupName)));
        MetadataColumnFiltersView.SortDescriptions.Add(new SortDescription(nameof(FilterOptionViewModel.GroupName), ListSortDirection.Ascending));
        MetadataColumnFiltersView.SortDescriptions.Add(new SortDescription(nameof(FilterOptionViewModel.Title), ListSortDirection.Ascending));
        MetadataColumnFiltersView.Filter = MetadataColumnVisible;

        _searchDebounceTimer = dispatcher.CreateTimer(SearchDebounce, OnSearchDebounceTick);
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _metadataColumnSearch = string.Empty;

    public RangeObservableCollection<FilterOptionViewModel> SourceFilters { get; }

    public RangeObservableCollection<FilterOptionViewModel> StatusFilters { get; }

    public RangeObservableCollection<FilterOptionViewModel> RelationFilters { get; }

    public RangeObservableCollection<FilterOptionViewModel> MetadataColumnFilters { get; }

    public ICollectionView MetadataColumnFiltersView { get; }

    public bool HasRelations => RelationFilters.Count > 0;

    public bool HasMetadataColumns => MetadataColumnFilters.Count > 0;

    public bool HasNoMatchingMetadataColumns => HasMetadataColumns && MetadataColumnFiltersView.IsEmpty;

    public event EventHandler? FiltersChanged;

    public event EventHandler? ColumnsAffected;

    internal void SetSearchImmediateForAutomation(string searchText)
    {
        SearchText = searchText;
        _searchDebounceTimer.Stop();
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void OnSearchDebounceTick()
    {
        _searchDebounceTimer.Stop();
        FiltersChanged?.Invoke(this, EventArgs.Empty);
        PersistFilters();
    }

    [RelayCommand]
    private void ClearMetadataColumnSearch()
    {
        MetadataColumnSearch = string.Empty;
    }

    partial void OnMetadataColumnSearchChanged(string value)
    {
        MetadataColumnFiltersView.Refresh();
        OnPropertyChanged(nameof(HasNoMatchingMetadataColumns));
    }

    private bool MetadataColumnVisible(object? item)
    {
        if (string.IsNullOrWhiteSpace(MetadataColumnSearch))
        {
            return true;
        }

        if (item is not FilterOptionViewModel option)
        {
            return false;
        }

        return option.Title.Contains(MetadataColumnSearch, StringComparison.CurrentCultureIgnoreCase)
            || (option.GroupName?.Contains(MetadataColumnSearch, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    public bool Matches(MediaRowViewModel row)
    {
        if (!string.IsNullOrWhiteSpace(SearchText)
            && row.SearchHaystack.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        var activeStatuses = StatusFilters.Where(f => f.IsChecked).Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (activeStatuses.Count > 0 && !row.LinkedStatusIds.Overlaps(activeStatuses))
        {
            return false;
        }

        var activeSources = SourceFilters.Where(f => f.IsChecked).Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (activeSources.Count > 0 && !row.LinkedSourceIds.Overlaps(activeSources))
        {
            return false;
        }

        if (_activeRelationSourceIds.Count > 0 && !row.LinkedSourceIds.Overlaps(_activeRelationSourceIds))
        {
            return false;
        }

        return true;
    }

    public HashSet<string> ActiveSourceIds() =>
        SourceFilters.Where(f => f.IsChecked).Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> ActiveRelationSourceIds() => _activeRelationSourceIds;

    public List<MetadataColumnInfo> SelectedMetadataColumns() =>
        MetadataColumnFilters
            .Where(f => f.IsChecked && _availableMetaById.ContainsKey(f.Id))
            .Select(f => _availableMetaById[f.Id])
            .ToList();

    public void FocusStatus(string statusId)
    {
        foreach (var filter in StatusFilters)
        {
            filter.IsChecked = string.Equals(filter.Id, statusId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void ApplyDomainData(List<Source> sources, List<Media> medias, List<SourceSyncRelation> relations)
    {
        _allSources.Clear();
        _allSources.AddRange(sources);
        _allMedias.Clear();
        _allMedias.AddRange(medias);
        _allRelations.Clear();
        _allRelations.AddRange(relations);

        RebuildFilterOptions();
        RebuildMetadataColumnOptions();
        RecomputeActiveRelationSources();
        OnPropertyChanged(nameof(HasRelations));
        OnPropertyChanged(nameof(HasMetadataColumns));
        OnPropertyChanged(nameof(HasNoMatchingMetadataColumns));
    }

    private void OnFilterChanged(FilterOptionViewModel option)
    {
        if (_suppressFilterPersist)
        {
            return;
        }

        if (SourceFilters.Contains(option))
        {
            ColumnsAffected?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            FiltersChanged?.Invoke(this, EventArgs.Empty);
        }

        PersistFilters();
    }

    private void OnRelationFilterChanged(FilterOptionViewModel option)
    {
        if (_suppressFilterPersist)
        {
            return;
        }

        RecomputeActiveRelationSources();

        ColumnsAffected?.Invoke(this, EventArgs.Empty);
        PersistFilters();
    }

    private void OnMetadataColumnChanged(FilterOptionViewModel option)
    {
        if (_suppressFilterPersist)
        {
            return;
        }

        ColumnsAffected?.Invoke(this, EventArgs.Empty);
        PersistFilters();
    }

    private void RebuildFilterOptions()
    {
        _suppressFilterPersist = true;

        try
        {
            var previousSources = SourceFilters.Where(f => f.IsChecked).Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var previousStatuses = StatusFilters.Where(f => f.IsChecked).Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var previousRelations = RelationFilters.Where(f => f.IsChecked).Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (previousSources.Count == 0)
            {
                previousSources = LoadSavedSet(SourceFilterSettingKey);
            }

            if (previousStatuses.Count == 0)
            {
                previousStatuses = LoadSavedSet(StatusFilterSettingKey);
            }

            if (previousRelations.Count == 0)
            {
                previousRelations = LoadSavedSet(RelationFilterSettingKey);
            }

            SourceFilters.ReplaceAll(_allSources
                .OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(source => new FilterOptionViewModel(source.Id, source.Title, OnFilterChanged)
                {
                    IsChecked = previousSources.Contains(source.Id),
                }));

            if (StatusFilters.Count == 0)
            {
                StatusFilters.AddRange(MediaStatusHelper.GetAll()
                    .OrderBy(s => s.Text, StringComparer.CurrentCultureIgnoreCase)
                    .Select(status => new FilterOptionViewModel(status.Id, status.Text, OnFilterChanged)
                    {
                        IsChecked = previousStatuses.Contains(status.Id),
                    }));
            }

            RelationFilters.ReplaceAll(_allRelations
                .Where(r => r.From is not null && r.To is not null)
                .OrderBy(r => r.From.TitleFull, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.To.TitleFull, StringComparer.CurrentCultureIgnoreCase)
                .Select(relation => new FilterOptionViewModel(
                    RelationId(relation),
                    $"{relation.From.TitleFull} → {relation.To.TitleFull}",
                    OnRelationFilterChanged)
                {
                    IsChecked = previousRelations.Contains(RelationId(relation)),
                }));

            var savedSearch = _settings.GetStringValue(SearchSettingKey);

            if (!string.IsNullOrEmpty(savedSearch) && string.IsNullOrEmpty(SearchText))
            {
                SearchText = savedSearch;
            }
        }
        finally
        {
            _suppressFilterPersist = false;
        }
    }

    private void RebuildMetadataColumnOptions()
    {
        _suppressFilterPersist = true;

        try
        {
            var previousSelected = MetadataColumnFilters.Where(f => f.IsChecked).Select(f => f.Id).ToHashSet(StringComparer.Ordinal);

            if (previousSelected.Count == 0)
            {
                previousSelected = LoadSavedSet(MetadataColumnsSettingKey);
            }

            var available = MediaMetadataColumns.Build(_allMedias, _allSources);

            _availableMetaById.Clear();

            foreach (var column in available)
            {
                _availableMetaById[column.ColumnId] = column;
            }

            MetadataColumnFilters.ReplaceAll(available
                .Select(column => new FilterOptionViewModel(column.ColumnId, column.ShortDisplayName, OnMetadataColumnChanged, column.GroupName)
                {
                    IsChecked = previousSelected.Contains(column.ColumnId),
                }));
        }
        finally
        {
            _suppressFilterPersist = false;
        }
    }

    private void RecomputeActiveRelationSources()
    {
        var checkedIds = RelationFilters.Where(f => f.IsChecked).Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (checkedIds.Count == 0)
        {
            _activeRelationSourceIds = new(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relation in _allRelations)
        {
            if (!checkedIds.Contains(RelationId(relation)))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(relation.FromId))
            {
                sourceIds.Add(relation.FromId);
            }

            if (!string.IsNullOrEmpty(relation.ToId))
            {
                sourceIds.Add(relation.ToId);
            }
        }

        _activeRelationSourceIds = sourceIds;
    }

    private static string RelationId(SourceSyncRelation relation) => $"{relation.FromId}>{relation.ToId}";

    private HashSet<string> LoadSavedSet(string key)
    {
        var raw = _settings.GetStringValue(key);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }

        return raw.Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void PersistFilters()
    {
        if (_suppressFilterPersist)
        {
            return;
        }

        _settings.SetValue(SearchSettingKey, SearchText ?? string.Empty);
        _settings.SetValue(SourceFilterSettingKey, JoinChecked(SourceFilters));
        _settings.SetValue(StatusFilterSettingKey, JoinChecked(StatusFilters));
        _settings.SetValue(RelationFilterSettingKey, JoinChecked(RelationFilters));
        _settings.SetValue(MetadataColumnsSettingKey, JoinChecked(MetadataColumnFilters));
    }

    private static string JoinChecked(IEnumerable<FilterOptionViewModel> options)
    {
        return string.Join(";", options.Where(o => o.IsChecked).Select(o => o.Id));
    }
}
