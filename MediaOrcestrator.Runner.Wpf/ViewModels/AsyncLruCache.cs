namespace MediaOrcestrator.Runner.Wpf.ViewModels;

internal sealed class AsyncLruCache<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly record struct CacheEntry(TKey Key, Task<TValue?> Task);

    private readonly int _capacity;
    private readonly object _sync = new();
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
    private readonly LinkedList<CacheEntry> _lru = new();

    public AsyncLruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);

        _capacity = capacity;
        _map = new(comparer);
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _map.Count;
            }
        }
    }

    public Task<TValue?> GetOrAddAsync(TKey key, Func<TKey, Task<TValue?>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        TaskCompletionSource<TValue?>? tcs = null;

        lock (_sync)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                var existingTask = existing.Value.Task;

                if (existingTask.IsCompleted && (!existingTask.IsCompletedSuccessfully || existingTask.Result is null))
                {
                    _lru.Remove(existing);
                    _map.Remove(key);
                }
                else
                {
                    _lru.Remove(existing);
                    _lru.AddFirst(existing);

                    return existingTask;
                }
            }

            tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var node = new LinkedListNode<CacheEntry>(new(key, tcs.Task));
            _lru.AddFirst(node);
            _map[key] = node;

            if (_map.Count > _capacity && _lru.Last is { } tail)
            {
                _lru.RemoveLast();
                _map.Remove(tail.Value.Key);
            }
        }

        _ = CompleteAsync(key, factory, tcs);

        return tcs.Task;
    }

    private async Task CompleteAsync(TKey key, Func<TKey, Task<TValue?>> factory, TaskCompletionSource<TValue?> tcs)
    {
        TValue? result = null;
        Exception? error = null;
        CancellationToken? canceledToken = null;

        try
        {
            result = await factory(key).ConfigureAwait(false);
        }
        catch (OperationCanceledException oce)
        {
            canceledToken = oce.CancellationToken;
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (canceledToken is not null || error is not null || result is null)
        {
            RemoveIfSame(key, tcs.Task);
        }

        if (canceledToken is { } token)
        {
            tcs.TrySetCanceled(token);
        }
        else if (error is not null)
        {
            tcs.TrySetException(error);
        }
        else
        {
            tcs.TrySetResult(result);
        }
    }

    private void RemoveIfSame(TKey key, Task<TValue?> expectedTask)
    {
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var node) && ReferenceEquals(node.Value.Task, expectedTask))
            {
                _map.Remove(key);
                _lru.Remove(node);
            }
        }
    }
}
