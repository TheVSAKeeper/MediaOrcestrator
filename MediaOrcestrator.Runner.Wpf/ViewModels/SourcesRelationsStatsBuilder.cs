using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public enum SourcesRelationsViewMode
{
    None = 0,
    List = 1,
    Graph = 2,
    Authentication = 3,
}

internal static class SourcesRelationsStatsBuilder
{
    public static readonly string[] SummaryPropertyNames =
    [
        nameof(SourcesRelationsViewModel.SourceCount),
        nameof(SourcesRelationsViewModel.DisabledSourceCount),
        nameof(SourcesRelationsViewModel.RelationCount),
        nameof(SourcesRelationsViewModel.DisabledRelationCount),
        nameof(SourcesRelationsViewModel.HasSources),
        nameof(SourcesRelationsViewModel.HasRelations),
        nameof(SourcesRelationsViewModel.HasAtLeastTwoSources),
        nameof(SourcesRelationsViewModel.HasPluginTypes),
        nameof(SourcesRelationsViewModel.ShowEmptySourcesState),
        nameof(SourcesRelationsViewModel.ShowEmptyRelationsState),
    ];

    public static Dictionary<string, SourceCardStats> BuildSourceStats(List<Source> sources, List<Media> medias)
    {
        var counters = new Dictionary<string, (int Total, int Ok, int PartialOk, int Missing, int Error, int Skipped)>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            counters[source.Id] = default;
        }

        foreach (var media in medias)
        {
            if (media.Sources is null)
            {
                continue;
            }

            foreach (var link in media.Sources)
            {
                if (!counters.TryGetValue(link.SourceId, out var c))
                {
                    continue;
                }

                c.Total++;

                switch (link.Status)
                {
                    case MediaStatus.Ok: c.Ok++; break;
                    case MediaStatus.PartialOk: c.PartialOk++; break;
                    case MediaStatus.Missing: c.Missing++; break;
                    case MediaStatus.Error: c.Error++; break;
                    case MediaStatus.Skipped: c.Skipped++; break;
                }

                counters[link.SourceId] = c;
            }
        }

        return counters.ToDictionary(
            kvp => kvp.Key,
            kvp => new SourceCardStats
            {
                Total = kvp.Value.Total,
                Ok = kvp.Value.Ok,
                PartialOk = kvp.Value.PartialOk,
                Missing = kvp.Value.Missing,
                Error = kvp.Value.Error,
                Skipped = kvp.Value.Skipped,
            },
            StringComparer.Ordinal);
    }

    public static Dictionary<(string From, string To), RelationCardStats> BuildRelationStats(
        List<SourceSyncRelation> relations,
        List<Media> medias)
    {
        var result = new Dictionary<(string From, string To), RelationCardStats>();

        foreach (var relation in relations)
        {
            if (string.IsNullOrEmpty(relation.FromId) || string.IsNullOrEmpty(relation.ToId))
            {
                continue;
            }

            var transferred = 0;
            var pending = 0;
            var failed = 0;

            foreach (var media in medias)
            {
                if (media.Sources is null)
                {
                    continue;
                }

                MediaSourceLink? fromLink = null;
                MediaSourceLink? toLink = null;

                foreach (var link in media.Sources)
                {
                    if (link.SourceId == relation.FromId)
                    {
                        fromLink = link;
                    }
                    else if (link.SourceId == relation.ToId)
                    {
                        toLink = link;
                    }
                }

                if (fromLink is null || fromLink.Status != MediaStatus.Ok)
                {
                    continue;
                }

                if (toLink is null)
                {
                    pending++;
                    continue;
                }

                switch (toLink.Status)
                {
                    case MediaStatus.Ok:
                    case MediaStatus.PartialOk:
                        transferred++;
                        break;
                    case MediaStatus.Error:
                        failed++;
                        break;
                    case MediaStatus.Missing:
                    case MediaStatus.None:
                        pending++;
                        break;
                }
            }

            result[(relation.FromId, relation.ToId)] = new()
            {
                Transferred = transferred,
                Pending = pending,
                Failed = failed,
            };
        }

        return result;
    }
}
