using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.Views.Media;

public partial class MediaGridView : UserControl, IView<MediaGridViewModel>
{
    private const double MinSourceColumnWidth = 64;
    private const double MaxDefaultSourceColumnWidth = 240;
    private const double MinCompactSourceColumnWidth = 30;
    private const double SourceHeaderChromePadding = 28;
    private const double MetadataColumnWidth = 140;

    private static readonly DependencyPropertyDescriptor ColumnWidthDescriptor =
        DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));

    private readonly List<DataGridColumn> _widthTrackedColumns = [];
    private readonly DispatcherTimer _columnWidthTimer;

    private MediaGridViewModel? _vm;

    public MediaGridView()
    {
        InitializeComponent();

        _columnWidthTimer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };

        _columnWidthTimer.Tick += OnColumnWidthTimerTick;

        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private static double CompactSourceColumnWidth(int headerLength) => 22 + headerLength * 8;

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            FlushColumnWidthSave();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        FlushColumnWidthSave();
    }

    private void FlushColumnWidthSave()
    {
        if (_columnWidthTimer.IsEnabled)
        {
            _columnWidthTimer.Stop();
            SaveColumnWidths();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ColumnsChanged -= OnColumnsChanged;
            _vm.AutoFitColumnsRequested -= OnAutoFitColumnsRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = e.NewValue as MediaGridViewModel;

        if (_vm is not null)
        {
            _vm.ColumnsChanged += OnColumnsChanged;
            _vm.AutoFitColumnsRequested += OnAutoFitColumnsRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
            RebuildColumns(_vm);
        }
    }

    private void OnAutoFitColumnsRequested(object? sender, EventArgs e) => AutoFitColumns();

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaGridViewModel.IsCompact) && _vm is not null)
        {
            UpdateTitleColumnStyle(_vm.IsCompact);
        }
    }

    private void AutoFitColumns()
    {
        if (_vm is null)
        {
            return;
        }

        var fitted = MediaGrid.Columns
            .Where(c => c.Header is Source or MetadataColumnInfo)
            .ToList();

        if (fitted.Count == 0)
        {
            return;
        }

        foreach (var column in fitted)
        {
            column.Width = column.Header is Source
                ? DataGridLength.SizeToHeader
                : DataGridLength.Auto;
        }

        MediaGrid.UpdateLayout();

        foreach (var column in fitted)
        {
            var width = column.ActualWidth;

            if (width > 0)
            {
                column.Width = new DataGridLength(width);
            }
        }

        SaveColumnWidths();
    }

    private void OnColumnsChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            RebuildColumns(_vm);
        }
    }

    private void OnColumnWidthChanged(object? sender, EventArgs e)
    {
        _columnWidthTimer.Stop();
        _columnWidthTimer.Start();
    }

    private void OnColumnWidthTimerTick(object? sender, EventArgs e)
    {
        _columnWidthTimer.Stop();
        SaveColumnWidths();
    }

    private void SaveColumnWidths()
    {
        if (_vm is null)
        {
            return;
        }

        foreach (var column in _widthTrackedColumns)
        {
            if (ColumnKey(column) is not { } key || key == MediaGridViewModel.TitleColumnKey)
            {
                continue;
            }

            var actual = column.ActualWidth;

            if (actual > 0 && Math.Abs(actual - DefaultColumnWidth(column)) > 1)
            {
                _vm.SetColumnWidth(key, actual);
            }
            else
            {
                _vm.ClearColumnWidth(key);
            }
        }
    }

    private void OnDataGridSorting(object sender, DataGridSortingEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var nextDirection = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        var sortKey = e.Column.Header switch
        {
            Source source => MediaGridViewModel.SourceColumnKey(source.Id),
            MetadataColumnInfo meta => MediaGridViewModel.MetadataColumnKey(meta.ColumnId),
            _ when MediaGrid.Columns.Count > 0 && ReferenceEquals(e.Column, MediaGrid.Columns[0])
                => MediaGridViewModel.TitleColumnKey,
            _ => null,
        };

        if (sortKey is null)
        {
            return;
        }

        e.Handled = true;
        ClearAllColumnSortMarkers();
        e.Column.SetCurrentValue(DataGridColumn.SortDirectionProperty, nextDirection);
        _vm.ApplySort(sortKey, nextDirection);
    }

    private void ClearAllColumnSortMarkers()
    {
        foreach (var col in MediaGrid.Columns)
        {
            col.SortDirection = null;
        }
    }

    private void OnDataGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null || MediaGrid.SelectedItem is not MediaRowViewModel row)
        {
            return;
        }

        var hit = VisualTreeHelper.HitTest(MediaGrid, e.GetPosition(MediaGrid));

        if (hit?.VisualHit is null || FindAncestor<DataGridRow>(hit.VisualHit) is null)
        {
            return;
        }

        _vm.OpenInspectorCommand.Execute(row);
    }

    private void OnDataGridContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var position = Mouse.GetPosition(MediaGrid);
        var hit = VisualTreeHelper.HitTest(MediaGrid, position);

        if (hit?.VisualHit is null)
        {
            e.Handled = true;
            return;
        }

        var cell = FindAncestor<DataGridCell>(hit.VisualHit);
        var row = FindAncestor<DataGridRow>(hit.VisualHit);
        var triggerRow = row?.Item as MediaRowViewModel;

        Source? specificSource = null;

        if (cell?.Column?.Header is Source src)
        {
            specificSource = src;
        }

        _vm.BuildContextMenu(triggerRow, specificSource);

        if (_vm.ContextMenuItems.Count == 0)
        {
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null && source is not T)
        {
            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return source as T;
    }
}
