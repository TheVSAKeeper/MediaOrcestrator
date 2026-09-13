using CommunityToolkit.Mvvm.ComponentModel;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MediaRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isCompact;

    public MediaRowViewModel(Media media, IReadOnlyList<Source> columnSources, IReadOnlyList<MetadataColumnInfo> metadataColumns, MediaSeriesEntry series)
    {
        Media = media;
        SeriesName = series.SeriesName;
        SeriesKey = series.SeriesKey;
        EpisodeNumber = series.EpisodeNumber;
        IsNoSeries = series.IsNoSeries;
        Cells = BuildCells(media, columnSources);
        MetadataCells = BuildMetadataCells(media, metadataColumns);
        LinkedSourceIds = media.Sources?.Select(l => l.SourceId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        LinkedStatusIds = media.Sources?.Select(l => l.Status ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SearchHaystack = BuildSearchHaystack(Title, Cells);
    }

    public Media Media { get; }

    public string MediaId => Media.Id;

    public string Title => Media.Title;

    public string SeriesName { get; }

    public string SeriesKey { get; }

    public int? EpisodeNumber { get; }

    public bool IsNoSeries { get; }

    public IReadOnlyList<MediaCellViewModel> Cells { get; }

    public IReadOnlyList<string> MetadataCells { get; }

    public HashSet<string> LinkedSourceIds { get; }

    public HashSet<string> LinkedStatusIds { get; }

    public int SourceCount => Cells.Count(c => c.HasLink);

    public int ColumnCount => Cells.Count;

    public IReadOnlyList<MediaCellViewModel> MissingCells => _missingCells ??= Cells.Where(c => !c.HasLink).ToList();

    private IReadOnlyList<MediaCellViewModel>? _missingCells;

    public int MissingCount => MissingCells.Count;

    public bool HasMissing => MissingCount > 0;

    public int ErrorCount => Cells.Count(c => c.HasLink && c.Status?.Id == MediaStatus.Error);

    public bool IsFullySynced => ColumnCount > 0 && MissingCount == 0 && ErrorCount == 0;

    public string StatusSummaryText
    {
        get
        {
            if (ColumnCount == 0)
            {
                return "Нет площадок";
            }

            if (IsFullySynced)
            {
                return $"Есть на всех {ColumnCount} площадках";
            }

            var parts = new List<string> { $"На {SourceCount} из {ColumnCount} площадок" };

            if (ErrorCount > 0)
            {
                parts.Add($"{ErrorCount} с ошибкой");
            }

            return string.Join(" · ", parts);
        }
    }

    public IReadOnlyList<MediaStatusTally> StatusHistogram => _histogram ??= BuildHistogram();

    private IReadOnlyList<MediaStatusTally>? _histogram;

    public string SearchHaystack { get; }

    private IReadOnlyList<MediaStatusTally> BuildHistogram()
    {
        var result = Cells
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

        if (MissingCount > 0)
        {
            result.Add(MediaStatusTally.Missing(MissingCount));
        }

        return result;
    }

    private static string BuildSearchHaystack(string title, IReadOnlyList<MediaCellViewModel> cells)
    {
        var builder = new System.Text.StringBuilder(title);

        foreach (var cell in cells)
        {
            var linkTitle = cell.Link?.Title;

            if (!string.IsNullOrEmpty(linkTitle))
            {
                builder.Append(' ').Append(linkTitle);
            }
        }

        foreach (var cell in cells)
        {
            var externalId = cell.Link?.ExternalId;

            if (!string.IsNullOrEmpty(externalId))
            {
                builder.Append(' ').Append(externalId);
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> BuildMetadataCells(Media media, IReadOnlyList<MetadataColumnInfo> metadataColumns)
    {
        if (metadataColumns.Count == 0)
        {
            return [];
        }

        var values = new string[metadataColumns.Count];

        for (var i = 0; i < metadataColumns.Count; i++)
        {
            values[i] = MediaMetadataColumns.Resolve(media, metadataColumns[i]);
        }

        return values;
    }

    private static IReadOnlyList<MediaCellViewModel> BuildCells(Media media, IReadOnlyList<Source> columnSources)
    {
        var linksBySource = (media.Sources ?? [])
            .GroupBy(l => l.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var cells = new List<MediaCellViewModel>(columnSources.Count);

        foreach (var source in columnSources)
        {
            linksBySource.TryGetValue(source.Id, out var link);
            cells.Add(new(source, link));
        }

        return cells;
    }
}

public sealed record MediaStatusTally(string StatusId, Brush Brush, int Count, string Label, int Order)
{
    public bool CanFilter => !string.IsNullOrEmpty(StatusId);

    public PackIconLucideKind Icon => StatusIcons.Resolve(StatusId);

    public static MediaStatusTally Missing(int count) =>
        new(string.Empty, Brushes.Gray, count, "нет на площадке", 99);

    public static int OrderOf(string? statusId) => statusId switch
    {
        MediaStatus.Ok => 0,
        MediaStatus.PartialOk => 1,
        MediaStatus.Skipped => 2,
        MediaStatus.None => 3,
        MediaStatus.Missing => 4,
        MediaStatus.Error => 5,
        _ => 10,
    };
}
