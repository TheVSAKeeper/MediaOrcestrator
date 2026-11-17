using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml;

namespace MediaOrcestrator.Runner.Wpf.Views.Media;

public partial class MediaGridView
{
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
            <iconPacks:PackIconLucide Kind="{{Binding Cells[{0}].Icon, Mode=OneTime}}"
                                      Foreground="{{Binding Cells[{0}].Brush, Mode=OneTime}}"
                                      ToolTip="{{Binding Cells[{0}].Tooltip, Mode=OneTime}}"
                                      Width="16"
                                      Height="16"
                                      HorizontalAlignment="Center"
                                      VerticalAlignment="Center" />
        </DataTemplate>
        """;

    private static readonly ConcurrentDictionary<int, DataTemplate> SourceCellTemplateCache = new();

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

        var titleCellStyle = GetTitleCellStyle(vm.IsCompact);
        var metadataCellTextStyle = (Style)FindResource("MetadataCellText");

        var titleCol = new DataGridTextColumn
        {
            Header = "Название",
            Width = new(1, DataGridLengthUnitType.Star),
            MinWidth = 200,
            Binding = new Binding(nameof(MediaRowViewModel.Title)) { Mode = BindingMode.OneTime },
            SortMemberPath = nameof(MediaRowViewModel.Title),
            ElementStyle = titleCellStyle,
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
                Binding = new Binding($"MetadataCells[{i}]") { Mode = BindingMode.OneTime },
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

    private Style GetTitleCellStyle(bool isCompact) =>
        (Style)FindResource(isCompact ? "MatrixCellCompactText" : "MatrixCellComfortableText");

    private void UpdateTitleColumnStyle(bool isCompact)
    {
        if (MediaGrid.Columns.Count > 0 && MediaGrid.Columns[0] is DataGridTextColumn titleCol)
        {
            titleCol.ElementStyle = GetTitleCellStyle(isCompact);
        }
    }

    private static DataTemplate BuildCellTemplate(int columnIndex) =>
        SourceCellTemplateCache.GetOrAdd(columnIndex, static index =>
        {
            var xaml = string.Format(CultureInfo.InvariantCulture, SourceCellTemplateXamlFormat, index);
            return ParseTemplate(xaml);
        });

    private static DataTemplate ParseTemplate(string xaml)
    {
        using var reader = new StringReader(xaml);
        using var xmlReader = XmlReader.Create(reader);
        return (DataTemplate)XamlReader.Load(xmlReader);
    }
}
