using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Modules;
using System.Collections.Concurrent;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

internal sealed class CommentsTreeBuilder(
    Orcestrator orcestrator,
    IUiDispatcher dispatcher)
{
    private const int VisibleRepliesPerRoot = 2;
    private const int BranchToggleThreshold = 3;

    public (IReadOnlyDictionary<string, Source> SourceById, IReadOnlyDictionary<(string SourceId, string ExternalMediaId), (Media Media, int SortNumber)> MediaByPair) BuildLookupIndexes()
    {
        var sourceById = orcestrator.GetSources().ToDictionary(s => s.Id);
        var mediaByPair = BuildMediaIndex();
        return (sourceById, mediaByPair);
    }

    public CommentsBuildResult BuildFromSnapshot(
        CommentsSnapshot snapshot,
        string search,
        CommentsLayoutMode layout,
        CommentsReplyStatusFilter replyStatus,
        IReadOnlySet<string> expandedBranches,
        CommentsTreeCallbacks callbacks,
        CancellationToken ct)
    {
        var records = FilterByReplyStatus(snapshot.Records, replyStatus);

        ct.ThrowIfCancellationRequested();

        var ctx = new TreeBuildContext(
            snapshot.SourceById,
            snapshot.MediaByPair,
            snapshot.MediaExternalLinkCache,
            expandedBranches,
            callbacks,
            search);

        return layout == CommentsLayoutMode.Grouped
            ? BuildGrouped(records, snapshot.Truncated, ctx)
            : BuildFlat(records, snapshot.Truncated, ctx);
    }

    private static IReadOnlyList<CommentRecord> FilterByReplyStatus(IReadOnlyList<CommentRecord> records, CommentsReplyStatusFilter filter)
    {
        if (filter == CommentsReplyStatusFilter.All || records.Count == 0)
        {
            return records;
        }

        var byId = new Dictionary<string, CommentRecord>(records.Count, StringComparer.Ordinal);

        foreach (var r in records)
        {
            byId.TryAdd(r.Id, r);
        }

        var childrenByParent = BuildChildrenIndex(records, byId);
        var result = new List<CommentRecord>();

        foreach (var root in records)
        {
            if (!IsRoot(root, byId) || root.IsAuthor)
            {
                continue;
            }

            var descendants = new List<CommentRecord>();
            CollectDescendants(root, childrenByParent, descendants);

            if (!MatchesReplyStatus(root, descendants, filter))
            {
                continue;
            }

            result.Add(root);
            result.AddRange(descendants);
        }

        return result;
    }

    private static bool IsRoot(CommentRecord r, Dictionary<string, CommentRecord> byId)
    {
        if (string.IsNullOrEmpty(r.ParentExternalCommentId))
        {
            return true;
        }

        var parentId = $"{r.SourceId}|{r.ExternalMediaId}|{r.ParentExternalCommentId}";
        return !byId.ContainsKey(parentId);
    }

    private static void CollectDescendants(
        CommentRecord node,
        Dictionary<string, List<CommentRecord>> childrenByParent,
        List<CommentRecord> output)
    {
        if (!childrenByParent.TryGetValue(node.Id, out var kids))
        {
            return;
        }

        foreach (var kid in kids)
        {
            output.Add(kid);
            CollectDescendants(kid, childrenByParent, output);
        }
    }

    private static bool MatchesReplyStatus(CommentRecord root, List<CommentRecord> descendants, CommentsReplyStatusFilter filter)
    {
        DateTime? latestAuthorReply = null;

        foreach (var d in descendants)
        {
            if (d.IsAuthor && (latestAuthorReply is null || d.PublishedAt > latestAuthorReply))
            {
                latestAuthorReply = d.PublishedAt;
            }
        }

        var hasAuthorReply = latestAuthorReply is not null;

        return filter switch
        {
            CommentsReplyStatusFilter.WithoutReply => !hasAuthorReply,
            CommentsReplyStatusFilter.WithReply => hasAuthorReply,
            CommentsReplyStatusFilter.NewReplies => hasAuthorReply
                                                    && descendants.Any(d => !d.IsAuthor && d.PublishedAt > latestAuthorReply!.Value),
            CommentsReplyStatusFilter.WithoutReplyAndLike => !hasAuthorReply && !root.LikedByAuthor,
            _ => true,
        };
    }

    private static CommentsBuildResult BuildFlat(IReadOnlyList<CommentRecord> records, bool truncated, TreeBuildContext ctx)
    {
        var byId = records.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var childrenByParent = BuildChildrenIndex(records, byId);
        var rows = new List<ICommentsRowViewModel>();
        var sortedRoots = records.Where(r => IsRoot(r, byId)).OrderByDescending(r => r.PublishedAt).ToList();

        foreach (var root in sortedRoots)
        {
            AppendItem(rows, root, 0, childrenByParent, showMediaThumbnail: true, CommentsLayoutMode.Flat, ctx);
            AppendChildren(rows, root, 1, childrenByParent, showMediaThumbnail: false, CommentsLayoutMode.Flat, ctx);
        }

        return new(rows, records.Count, truncated);
    }

    private CommentsBuildResult BuildGrouped(IReadOnlyList<CommentRecord> records, bool truncated, TreeBuildContext ctx)
    {
        var groups = records
            .GroupBy(r => ResolveGroupKey(r, ctx.MediaByPair))
            .Select(g =>
            {
                var sample = g.First();
                var media = ctx.MediaByPair.TryGetValue((sample.SourceId, sample.ExternalMediaId), out var info) ? info.Media : null;
                var title = media?.Title ?? $"<{sample.ExternalMediaId}>";
                var sortNumber = media is null
                    ? int.MaxValue
                    : g.Min(r => ctx.MediaByPair.TryGetValue((r.SourceId, r.ExternalMediaId), out var info2) ? info2.SortNumber : int.MaxValue);

                return new { Key = g.Key, Title = title, Records = g.ToList(), SortNumber = sortNumber, Media = media };
            })
            .OrderByDescending(g => g.SortNumber)
            .ThenByDescending(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<ICommentsRowViewModel>();

        foreach (var grp in groups)
        {
            var sources = grp.Records
                .GroupBy(r => r.SourceId)
                .Select(s =>
                {
                    var src = ctx.SourceById.GetValueOrDefault(s.Key);
                    var firstExt = s.First().ExternalMediaId;

                    return new MediaGroupSourceInfo(
                        s.Key,
                        firstExt,
                        src?.TitleFull ?? s.Key,
                        HasMediaExternalLink(ctx, s.Key, firstExt),
                        src?.Type is ISupportsComments);
                })
                .OrderBy(s => s.SourceTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();

            rows.Add(new MediaGroupHeaderViewModel(
                grp.Key,
                grp.Title,
                grp.Records.Count,
                sources,
                ctx.Callbacks.RefreshGroup,
                ctx.Callbacks.RefreshOneSource,
                ctx.Callbacks.OpenOneSource,
                dispatcher,
                ResolveThumbnailUrl(grp.Media)));

            var groupById = grp.Records.ToDictionary(r => r.Id, StringComparer.Ordinal);
            var groupChildren = BuildChildrenIndex(grp.Records, groupById);
            var groupRoots = grp.Records.Where(r => IsRoot(r, groupById)).OrderByDescending(r => r.PublishedAt).ToList();

            foreach (var root in groupRoots)
            {
                AppendItem(rows, root, 0, groupChildren, showMediaThumbnail: false, CommentsLayoutMode.Grouped, ctx);
                AppendChildren(rows, root, 1, groupChildren, showMediaThumbnail: false, CommentsLayoutMode.Grouped, ctx);
            }
        }

        return new(rows, records.Count, truncated);
    }

    private static void AppendItem(
        List<ICommentsRowViewModel> rows,
        CommentRecord record,
        int depth,
        Dictionary<string, List<CommentRecord>> childrenByParent,
        bool showMediaThumbnail,
        CommentsLayoutMode layout,
        TreeBuildContext ctx)
    {
        var itemContext = BuildContext(record, depth, childrenByParent, showMediaThumbnail, layout, ctx);
        rows.Add(new CommentItemViewModel(record, itemContext, ctx.Callbacks.ItemHost, ctx.Search));
    }

    private static void AppendChildren(
        List<ICommentsRowViewModel> rows,
        CommentRecord parent,
        int depth,
        Dictionary<string, List<CommentRecord>> childrenByParent,
        bool showMediaThumbnail,
        CommentsLayoutMode layout,
        TreeBuildContext ctx)
    {
        if (!childrenByParent.TryGetValue(parent.Id, out var kids))
        {
            return;
        }

        var sorted = kids.OrderBy(r => r.PublishedAt).ToList();

        if (depth == 1 && sorted.Count > BranchToggleThreshold)
        {
            var isExpanded = ctx.ExpandedBranches.Contains(parent.Id);
            var visible = isExpanded ? sorted : sorted.Take(VisibleRepliesPerRoot).ToList();

            foreach (var kid in visible)
            {
                AppendItem(rows, kid, depth, childrenByParent, showMediaThumbnail, layout, ctx);
                AppendChildren(rows, kid, depth + 1, childrenByParent, showMediaThumbnail, layout, ctx);
            }

            rows.Add(new BranchToggleViewModel(parent.Id, sorted.Count, sorted.Count - VisibleRepliesPerRoot, depth, isExpanded, ctx.Callbacks.ToggleBranch));
            return;
        }

        foreach (var kid in sorted)
        {
            AppendItem(rows, kid, depth, childrenByParent, showMediaThumbnail, layout, ctx);
            AppendChildren(rows, kid, depth + 1, childrenByParent, showMediaThumbnail, layout, ctx);
        }
    }

    private static CommentItemContext BuildContext(
        CommentRecord record,
        int depth,
        Dictionary<string, List<CommentRecord>> childrenByParent,
        bool showMediaThumbnail,
        CommentsLayoutMode layout,
        TreeBuildContext ctx)
    {
        var source = ctx.SourceById.GetValueOrDefault(record.SourceId);
        var media = ctx.MediaByPair.TryGetValue((record.SourceId, record.ExternalMediaId), out var info) ? info.Media : null;
        var thumbnailUrl = showMediaThumbnail ? ResolveThumbnailUrl(media) : null;

        return new(
            depth,
            source?.TitleFull ?? record.SourceId,
            media?.Title ?? $"<{record.ExternalMediaId}>",
            source?.Type is ISupportsCommentPermalinks && !string.IsNullOrEmpty(record.ExternalCommentId) && !string.IsNullOrEmpty(record.ExternalMediaId),
            source?.Type is ISupportsCommentMutations,
            source?.Type is ISupportsCommentLikes,
            source?.Type is ISupportsCommentAuthors,
            HasMediaExternalLink(ctx, record.SourceId, record.ExternalMediaId),
            CountDescendants(record.Id, childrenByParent),
            layout,
            thumbnailUrl,
            showMediaThumbnail && depth == 0 && !string.IsNullOrEmpty(thumbnailUrl));
    }

    private static bool HasMediaExternalLink(TreeBuildContext ctx, string sourceId, string externalMediaId)
    {
        if (string.IsNullOrEmpty(externalMediaId))
        {
            return false;
        }

        return ctx.MediaExternalLinkCache.GetOrAdd((sourceId, externalMediaId), key =>
        {
            if (!ctx.SourceById.TryGetValue(key.SourceId, out var src) || src.Type is null)
            {
                return false;
            }

            try
            {
                return src.Type.GetExternalUri(key.ExternalMediaId, src.Settings) is not null;
            }
            catch
            {
                return false;
            }
        });
    }

    private static int CountDescendants(string id, Dictionary<string, List<CommentRecord>> childrenByParent)
    {
        if (!childrenByParent.TryGetValue(id, out var kids))
        {
            return 0;
        }

        var total = kids.Count;

        foreach (var kid in kids)
        {
            total += CountDescendants(kid.Id, childrenByParent);
        }

        return total;
    }

    private static Dictionary<string, List<CommentRecord>> BuildChildrenIndex(IEnumerable<CommentRecord> records, Dictionary<string, CommentRecord> byId)
    {
        var index = new Dictionary<string, List<CommentRecord>>(StringComparer.Ordinal);

        foreach (var r in records)
        {
            if (string.IsNullOrEmpty(r.ParentExternalCommentId))
            {
                continue;
            }

            var parentId = $"{r.SourceId}|{r.ExternalMediaId}|{r.ParentExternalCommentId}";

            if (!byId.ContainsKey(parentId))
            {
                continue;
            }

            if (!index.TryGetValue(parentId, out var bucket))
            {
                bucket = [];
                index[parentId] = bucket;
            }

            bucket.Add(r);
        }

        return index;
    }

    private Dictionary<(string SourceId, string ExternalMediaId), (Media Media, int SortNumber)> BuildMediaIndex()
    {
        var dict = new Dictionary<(string, string), (Media, int)>();

        foreach (var media in orcestrator.GetMedias())
        {
            foreach (var link in media.Sources)
            {
                if (link is { SourceId: not null, ExternalId: not null })
                {
                    dict[(link.SourceId, link.ExternalId)] = (media, link.SortNumber);
                }
            }
        }

        return dict;
    }

    private static string? ResolveThumbnailUrl(Media? media) =>
        media?.Metadata.FirstOrDefault(m => m.Key == "PreviewUrl" && !string.IsNullOrEmpty(m.Value))?.Value;

    private static string ResolveGroupKey(CommentRecord r, IReadOnlyDictionary<(string, string), (Media Media, int SortNumber)> mediaByPair) =>
        mediaByPair.TryGetValue((r.SourceId, r.ExternalMediaId), out var info)
            ? info.Media.Id
            : $"__orphan__|{r.SourceId}|{r.ExternalMediaId}";

    private sealed record TreeBuildContext(
        IReadOnlyDictionary<string, Source> SourceById,
        IReadOnlyDictionary<(string SourceId, string ExternalMediaId), (Media Media, int SortNumber)> MediaByPair,
        ConcurrentDictionary<(string SourceId, string ExternalMediaId), bool> MediaExternalLinkCache,
        IReadOnlySet<string> ExpandedBranches,
        CommentsTreeCallbacks Callbacks,
        string Search);
}

internal sealed record CommentsTreeCallbacks(
    ICommentItemHost ItemHost,
    Func<BranchToggleViewModel, Task> ToggleBranch,
    Func<MediaGroupHeaderViewModel, Task> RefreshGroup,
    Func<MediaGroupSourceActionViewModel, Task> RefreshOneSource,
    Func<MediaGroupSourceActionViewModel, Task> OpenOneSource);

internal sealed record CommentsBuildResult(List<ICommentsRowViewModel> Rows, int TotalCount, bool Truncated);
