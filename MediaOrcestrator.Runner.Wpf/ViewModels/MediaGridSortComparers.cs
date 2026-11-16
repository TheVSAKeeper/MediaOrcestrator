using MediaOrcestrator.Domain;
using System.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

internal static class MediaGridSortComparers
{
    public static Comparer<MediaRowViewModel>? BuildColumnComparer(
        string? activeSortKey,
        ListSortDirection activeSortDirection,
        string titleColumnKey,
        string metadataColumnKeyPrefix,
        string sourceColumnKeyPrefix,
        Func<string, int> indexOfMetadataColumn,
        Func<string, int> indexOfSourceColumn)
    {
        if (activeSortKey is null)
        {
            return null;
        }

        var sign = activeSortDirection == ListSortDirection.Ascending ? 1 : -1;

        if (activeSortKey == titleColumnKey)
        {
            return CreateTitleComparer(sign);
        }

        if (activeSortKey.StartsWith(metadataColumnKeyPrefix, StringComparison.Ordinal))
        {
            var index = indexOfMetadataColumn(activeSortKey[metadataColumnKeyPrefix.Length..]);
            return index >= 0 ? CreateMetadataComparer(index, sign) : null;
        }

        if (activeSortKey.StartsWith(sourceColumnKeyPrefix, StringComparison.Ordinal))
        {
            var index = indexOfSourceColumn(activeSortKey[sourceColumnKeyPrefix.Length..]);
            return index >= 0 ? CreateSourceComparer(index, sign) : null;
        }

        return null;
    }

    public static Comparer<MediaRowViewModel> BuildGroupedComparer(Comparer<MediaRowViewModel>? inner) =>
        Comparer<MediaRowViewModel>.Create((a, b) =>
        {
            var bySeries = CompareSeries(a, b);
            return bySeries != 0
                ? bySeries
                : inner?.Compare(a, b) ?? CompareEpisodeThenTitle(a, b);
        });

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
}
