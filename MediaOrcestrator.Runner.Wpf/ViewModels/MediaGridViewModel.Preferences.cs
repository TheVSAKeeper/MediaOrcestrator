using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MediaGridViewModel
{
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
            _settings.SetValue(SourceHeaderCompactLengthSettingKey, value.ToString(CultureInfo.InvariantCulture));
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

        var inner = MediaGridSortComparers.BuildColumnComparer(
            ActiveSortKey,
            ActiveSortDirection,
            TitleColumnKey,
            MetadataColumnKeyPrefix,
            SourceColumnKeyPrefix,
            IndexOfMetadataColumn,
            IndexOfSourceColumn);

        list.CustomSort = IsGrouped ? MediaGridSortComparers.BuildGroupedComparer(inner) : inner;
    }

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

    private void PersistColumnWidths()
    {
        var text = string.Join(";", _columnWidths
            .Select(kv => $"{kv.Key}={kv.Value.ToString("F0", CultureInfo.InvariantCulture)}"));

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

            if (int.TryParse(
                    _settings.GetStringValue(SourceHeaderCompactLengthSettingKey),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var headerLength))
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

            if (double.TryParse(entry[(eq + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                && width >= MinPersistedColumnWidth
                && width <= MaxPersistedColumnWidth)
            {
                _columnWidths[entry[..eq]] = width;
            }
        }
    }
}
