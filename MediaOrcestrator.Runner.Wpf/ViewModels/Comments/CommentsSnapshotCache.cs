using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using System.Collections.Concurrent;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

internal sealed class CommentsSnapshotCache
{
    private readonly object _gate = new();
    private long _generation;
    private long _hits;
    private long _misses;
    private CommentsSnapshotKey? _key;
    private CommentsSnapshot? _snapshot;

    public long Hits
    {
        get
        {
            lock (_gate)
            {
                return _hits;
            }
        }
    }

    public long Misses
    {
        get
        {
            lock (_gate)
            {
                return _misses;
            }
        }
    }

    public long CaptureGeneration()
    {
        lock (_gate)
        {
            return _generation;
        }
    }

    public bool TryGet(CommentsSnapshotKey key, out CommentsSnapshot? snapshot)
    {
        lock (_gate)
        {
            if (_snapshot is not null && _key is not null && _key.Equals(key))
            {
                _hits++;
                snapshot = _snapshot;
                return true;
            }

            _misses++;
            snapshot = null;
            return false;
        }
    }

    public void Set(CommentsSnapshotKey key, CommentsSnapshot snapshot, long expectedGeneration)
    {
        lock (_gate)
        {
            if (_generation != expectedGeneration)
            {
                return;
            }

            _key = key;
            _snapshot = snapshot;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _generation++;
            _key = null;
            _snapshot = null;
        }
    }
}

internal sealed class CommentsSnapshotKey : IEquatable<CommentsSnapshotKey>
{
    private readonly string[] _sortedSourceIds;

    public CommentsSnapshotKey(IReadOnlyCollection<string>? sourceIds, string search, int limit)
    {
        _sortedSourceIds = sourceIds is { Count: > 0 }
            ? sourceIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray()
            : [];
        Search = search;
        Limit = limit;
    }

    public string Search { get; }

    public int Limit { get; }

    public bool Equals(CommentsSnapshotKey? other)
    {
        if (other is null)
        {
            return false;
        }

        return Limit == other.Limit
            && string.Equals(Search, other.Search, StringComparison.Ordinal)
            && _sortedSourceIds.AsSpan().SequenceEqual(other._sortedSourceIds);
    }

    public override bool Equals(object? obj) => Equals(obj as CommentsSnapshotKey);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Search, StringComparer.Ordinal);
        hash.Add(Limit);

        foreach (var id in _sortedSourceIds)
        {
            hash.Add(id, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}

internal sealed class CommentsSnapshot(
    IReadOnlyList<CommentRecord> records,
    bool truncated,
    IReadOnlyDictionary<string, Source> sourceById,
    IReadOnlyDictionary<(string SourceId, string ExternalMediaId), (Media Media, int SortNumber)> mediaByPair)
{
    public IReadOnlyList<CommentRecord> Records { get; } = records;

    public bool Truncated { get; } = truncated;

    public IReadOnlyDictionary<string, Source> SourceById { get; } = sourceById;

    public IReadOnlyDictionary<(string SourceId, string ExternalMediaId), (Media Media, int SortNumber)> MediaByPair { get; } = mediaByPair;

    public ConcurrentDictionary<(string SourceId, string ExternalMediaId), bool> MediaExternalLinkCache { get; } = new();
}
