using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.Views.Media;

public partial class MediaGridView : UserControl, IView<MediaGridViewModel>
{
    private const double MinSourceColumnWidth = 64;
    private const double MaxDefaultSourceColumnWidth = 240;
    private const double SortIndicatorWidth = 12;
    private const double MinCompactSourceColumnWidth = 36 + SortIndicatorWidth;
    private const double SourceHeaderChromePadding = 28;
    private const double MetadataColumnWidth = 140;

    private static readonly DependencyPropertyDescriptor ColumnWidthDescriptor =
        DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));

    private readonly List<DataGridColumn> _widthTrackedColumns = [];
    private readonly DispatcherTimer _columnWidthTimer;

    private MediaGridViewModel? _vm;
    private bool _applyingSelection;

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

    private static double CompactSourceColumnWidth(int headerLength) => 28 + SortIndicatorWidth + headerLength * 8;

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
            _vm.SelectionRequested -= OnSelectionRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = e.NewValue as MediaGridViewModel;

        if (_vm is not null)
        {
            _vm.ColumnsChanged += OnColumnsChanged;
            _vm.AutoFitColumnsRequested += OnAutoFitColumnsRequested;
            _vm.SelectionRequested += OnSelectionRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
            RebuildColumns(_vm);
            ApplyRequestedSelection();
        }
    }

    private void OnItemsSourceUpdated(object sender, DataTransferEventArgs e)
    {
        if (ReferenceEquals(e.TargetObject, MediaGrid) && e.Property == ItemsControl.ItemsSourceProperty)
        {
            ApplyRequestedSelection();
        }
    }

    private void OnSelectionRequested(object? sender, EventArgs e) => ApplyRequestedSelection();

    private void ApplyRequestedSelection()
    {
        if (_vm is null || !ReferenceEquals(MediaGrid.ItemsSource, _vm.RowsView)
            || MediaGrid.SelectedItems.OfType<MediaRowViewModel>().SequenceEqual(_vm.SelectedRows))
        {
            return;
        }

        _applyingSelection = true;
        try
        {
            MediaGrid.UnselectAll();
            foreach (var row in _vm.SelectedRows)
            {
                MediaGrid.SelectedItems.Add(row);
            }
        }
        finally
        {
            _applyingSelection = false;
        }
    }

    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_applyingSelection && _vm is not null && ReferenceEquals(e.OriginalSource, MediaGrid)
            && ReferenceEquals(MediaGrid.ItemsSource, _vm.RowsView))
        {
            _vm.ApplySelectionChange(e.RemovedItems.OfType<MediaRowViewModel>(), e.AddedItems.OfType<MediaRowViewModel>());
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
        if (_vm is null || e.ChangedButton != MouseButton.Left || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var hit = VisualTreeHelper.HitTest(MediaGrid, e.GetPosition(MediaGrid));

        if (FindAncestor<DataGridRow>(hit?.VisualHit)?.Item is not MediaRowViewModel row)
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

        var target = e.CursorLeft < 0 && e.CursorTop < 0
            ? e.OriginalSource as DependencyObject
            : VisualTreeHelper.HitTest(MediaGrid, Mouse.GetPosition(MediaGrid))?.VisualHit;

        if (target is null)
        {
            e.Handled = true;
            return;
        }

        var cell = FindAncestor<DataGridCell>(target);
        var row = FindAncestor<DataGridRow>(target);
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
