using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;

namespace MediaOrcestrator.Runner.Wpf.Views.Media;

public partial class MediaGridView : UserControl, IView<MediaGridViewModel>
{
    private const double MinSourceColumnWidth = 64;
    private const double MaxDefaultSourceColumnWidth = 240;
    private const double MinCompactSourceColumnWidth = 30;
    private const double SourceHeaderChromePadding = 28;
    private const double MetadataColumnWidth = 140;

    private static double CompactSourceColumnWidth(int headerLength) => 22 + headerLength * 8;

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
        }

        _vm = e.NewValue as MediaGridViewModel;

        if (_vm is not null)
        {
            _vm.ColumnsChanged += OnColumnsChanged;
            _vm.AutoFitColumnsRequested += OnAutoFitColumnsRequested;
            RebuildColumns(_vm);
        }
    }

    private void OnAutoFitColumnsRequested(object? sender, EventArgs e) => AutoFitColumns();

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

    private const string SourceHeaderTemplateXaml =
        """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
            <TextBlock Text="{Binding Title}"
                       ToolTip="{Binding TitleFull}"
                       TextAlignment="Center"
                       TextWrapping="NoWrap"
                       TextTrimming="CharacterEllipsis"
                       HorizontalAlignment="Stretch" />
        </DataTemplate>
        """;

    private const string CompactSourceHeaderTemplateXamlFormat =
        """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                      xmlns:conv="clr-namespace:KeepShell.Converters;assembly=KeepShell">
            <DataTemplate.Resources>
                <conv:TruncateTextConverter x:Key="Truncate" />
            </DataTemplate.Resources>
            <TextBlock Text="{{Binding Title, Converter={{StaticResource Truncate}}, ConverterParameter={0}}}"
                       ToolTip="{{Binding TitleFull}}"
                       TextAlignment="Center"
                       TextWrapping="NoWrap"
                       TextTrimming="CharacterEllipsis"
                       HorizontalAlignment="Stretch" />
        </DataTemplate>
        """;

    private const string MetadataHeaderTemplateXaml =
        """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
            <TextBlock Text="{Binding DisplayName}"
                       ToolTip="{Binding DisplayName}"
                       TextWrapping="NoWrap"
                       TextTrimming="CharacterEllipsis"
                       HorizontalAlignment="Stretch" />
        </DataTemplate>
        """;

    private const string SourceCellTemplateXamlFormat =
        """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:iconPacks="http://metro.mahapps.com/winfx/xaml/iconpacks">
            <iconPacks:PackIconLucide Kind="{{Binding Cells[{0}].Icon}}"
                                      Foreground="{{Binding Cells[{0}].Brush}}"
                                      ToolTip="{{Binding Cells[{0}].Tooltip}}"
                                      Width="16"
                                      Height="16"
                                      HorizontalAlignment="Center"
                                      VerticalAlignment="Center" />
        </DataTemplate>
        """;

    private static readonly DataTemplate SourceHeaderTemplate = ParseTemplate(SourceHeaderTemplateXaml);

    private static readonly DataTemplate MetadataHeaderTemplate = ParseTemplate(MetadataHeaderTemplateXaml);

    private static DataTemplate BuildCompactSourceHeaderTemplate(int headerLength)
    {
        var xaml = string.Format(CultureInfo.InvariantCulture, CompactSourceHeaderTemplateXamlFormat, headerLength);
        return ParseTemplate(xaml);
    }

    private void RebuildColumns(MediaGridViewModel vm)
    {
        DetachColumnWidthTracking();
        MediaGrid.Columns.Clear();

        var cellTextStyle = (Style)FindResource("MatrixCellText");
        var metadataCellTextStyle = (Style)FindResource("MetadataCellText");

        var titleCol = new DataGridTextColumn
        {
            Header = "Название",
            Width = new(1, DataGridLengthUnitType.Star),
            MinWidth = 200,
            Binding = new Binding(nameof(MediaRowViewModel.Title)),
            SortMemberPath = nameof(MediaRowViewModel.Title),
            ElementStyle = cellTextStyle,
        };
        MediaGrid.Columns.Add(titleCol);

        for (var i = 0; i < vm.MetadataColumns.Count; i++)
        {
            var meta = vm.MetadataColumns[i];
            var key = MediaGridViewModel.MetadataColumnKey(meta.ColumnId);
            var col = new DataGridTextColumn
            {
                Header = meta,
                HeaderTemplate = MetadataHeaderTemplate,
                Width = vm.TryGetColumnWidth(key, out var metaWidth) ? new(metaWidth) : new(MetadataColumnWidth),
                MinWidth = 80,
                Binding = new Binding($"MetadataCells[{i}]"),
                CanUserSort = true,
                ElementStyle = metadataCellTextStyle,
            };
            MediaGrid.Columns.Add(col);
            TrackColumnWidth(col);
        }

        var sourceHeaderTemplate = vm.IsSourceHeaderCompact
            ? BuildCompactSourceHeaderTemplate(vm.SourceHeaderCompactLength)
            : SourceHeaderTemplate;

        var minWidth = vm.IsSourceHeaderCompact
            ? MinCompactSourceColumnWidth
            : MinSourceColumnWidth;

        var compactWidth = CompactSourceColumnWidth(vm.SourceHeaderCompactLength);

        for (var i = 0; i < vm.ColumnSources.Count; i++)
        {
            var source = vm.ColumnSources[i];
            var key = MediaGridViewModel.SourceColumnKey(source.Id);
            var defaultWidth = vm.IsSourceHeaderCompact
                ? compactWidth
                : MeasureSourceColumnWidth(source);
            var hasSavedWidth = vm.TryGetColumnWidth(key, out var srcWidth);
            var colMinWidth = vm.IsSourceHeaderCompact || hasSavedWidth
                ? minWidth
                : defaultWidth;
            var col = new DataGridTemplateColumn
            {
                Header = source,
                MinWidth = colMinWidth,
                Width = hasSavedWidth
                    ? new(Math.Max(minWidth, srcWidth))
                    : new(defaultWidth),
                CellTemplate = BuildCellTemplate(i),
                HeaderTemplate = sourceHeaderTemplate,
                SortMemberPath = nameof(MediaRowViewModel.Title),
                CanUserSort = true,
            };
            MediaGrid.Columns.Add(col);
            TrackColumnWidth(col);
        }

        ApplySortIndicator();
    }

    private void TrackColumnWidth(DataGridColumn column)
    {
        ColumnWidthDescriptor.AddValueChanged(column, OnColumnWidthChanged);
        _widthTrackedColumns.Add(column);
    }

    private void DetachColumnWidthTracking()
    {
        foreach (var column in _widthTrackedColumns)
        {
            ColumnWidthDescriptor.RemoveValueChanged(column, OnColumnWidthChanged);
        }

        _widthTrackedColumns.Clear();
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

    private double MeasureSourceColumnWidth(Source source)
    {
        var fontFamily = TryFindResource("Font.Sans") as FontFamily ?? MediaGrid.FontFamily;
        var fontSize = TryFindResource("Font.Size.M") is double size && size > 0
            ? size
            : MediaGrid.FontSize;
        var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formatted = new FormattedText(
            source.Title,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            pixelsPerDip);
        var maxWidth = MaxDefaultSourceColumnWidth * Math.Max(1.0, fontSize / 14.0);
        return Math.Clamp(Math.Ceiling(formatted.WidthIncludingTrailingWhitespace + SourceHeaderChromePadding), MinSourceColumnWidth, maxWidth);
    }

    private double DefaultColumnWidth(DataGridColumn column) => column.Header switch
    {
        Source source => _vm is { IsSourceHeaderCompact: true } vm
            ? CompactSourceColumnWidth(vm.SourceHeaderCompactLength)
            : MeasureSourceColumnWidth(source),
        MetadataColumnInfo => MetadataColumnWidth,
        _ => 0,
    };

    private static string? ColumnKey(DataGridColumn column) => column.Header switch
    {
        Source source => MediaGridViewModel.SourceColumnKey(source.Id),
        MetadataColumnInfo meta => MediaGridViewModel.MetadataColumnKey(meta.ColumnId),
        _ => MediaGridViewModel.TitleColumnKey,
    };

    private void ApplySortIndicator()
    {
        var activeKey = _vm?.ActiveSortKey;

        foreach (var column in MediaGrid.Columns)
        {
            column.SortDirection = activeKey is not null && ColumnKey(column) == activeKey
                ? _vm!.ActiveSortDirection
                : null;
        }
    }

    private static DataTemplate BuildCellTemplate(int columnIndex)
    {
        var xaml = string.Format(CultureInfo.InvariantCulture, SourceCellTemplateXamlFormat, columnIndex);
        return ParseTemplate(xaml);
    }

    private static DataTemplate ParseTemplate(string xaml)
    {
        using var reader = new StringReader(xaml);
        using var xmlReader = XmlReader.Create(reader);
        return (DataTemplate)XamlReader.Load(xmlReader);
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
