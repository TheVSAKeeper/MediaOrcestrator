using System.Collections.Concurrent;

namespace MediaOrcestrator.Modules;

public sealed class SessionFileAccess
{
    private readonly ConcurrentDictionary<string, SessionGate> _gates = new(StringComparer.OrdinalIgnoreCase);

    public Task<IDisposable> ReadAsync(string path, CancellationToken cancellationToken) =>
        GetGate(path).EnterAsync(exclusive: false, cancellationToken);

    public Task<IDisposable> WriteAsync(string path, CancellationToken cancellationToken) =>
        GetGate(path).EnterAsync(exclusive: true, cancellationToken);

    private SessionGate GetGate(string path) => _gates.GetOrAdd(Path.GetFullPath(path), _ => new());

    private sealed class SessionGate
    {
        private readonly SemaphoreSlim _admission = new(1, 1);
        private readonly object _sync = new();
        private int _readers;
        private TaskCompletionSource? _drained;

        public async Task<IDisposable> EnterAsync(bool exclusive, CancellationToken cancellationToken)
        {
            await _admission.WaitAsync(cancellationToken);
            var writerAdmitted = false;
            try
            {
                Task? drain;
                lock (_sync)
                {
                    if (!exclusive)
                    {
                        _readers++;
                        return new Lease(ReleaseReader);
                    }

                    drain = _readers == 0 ? null : (_drained ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                }

                if (drain is not null)
                {
                    await drain.WaitAsync(cancellationToken);
                }

                writerAdmitted = true;
                return new Lease(() => _admission.Release());
            }
            finally
            {
                if (!writerAdmitted)
                {
                    _admission.Release();
                }
            }
        }

        private void ReleaseReader()
        {
            lock (_sync)
            {
                if (--_readers == 0)
                {
                    _drained?.SetResult();
                    _drained = null;
                }
            }
        }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
