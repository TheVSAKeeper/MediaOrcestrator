using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MediaOrcestrator.Domain;

public class ActionHolder(ILogger<ActionHolder> logger)
{
    private readonly ConcurrentDictionary<Guid, RunningAction> _actions = new();

    public event EventHandler? Changed;

    public IReadOnlyList<RunningAction> Snapshot()
    {
        return _actions.Values.ToArray();
    }

    public RunningAction Register(string name, string status, int progressMax, CancellationTokenSource ctx)
    {
        var id = Guid.NewGuid();
        var act = new RunningAction
        {
            Id = id,
            Name = name,
            Status = status,
            ProgressMax = progressMax,
            CancellationTokenSource = ctx,
            Holder = this,
        };

        _actions.TryAdd(id, act);
        logger.LogInformation("Action registered: {Id} {Name}", id, name);
        OnChanged();
        return act;
    }

    public void SetStatus(Guid id, string value)
    {
        if (_actions.TryGetValue(id, out var act))
        {
            act.Status = value;
        }
    }

    public void ProgressPlus(Guid id)
    {
        if (_actions.TryGetValue(id, out var act))
        {
            act.IncrementProgress();
        }
    }

    public void Cancel(Guid id)
    {
        if (!_actions.TryRemove(id, out var act))
        {
            return;
        }

        logger.LogWarning("Action cancelled: {Id} {Name}", act.Id, act.Name);

        try
        {
            act.CancellationTokenSource.Cancel();
        }
        finally
        {
            act.CancellationTokenSource.Dispose();
        }

        OnChanged();
    }

    internal void Remove(Guid id)
    {
        if (!_actions.TryRemove(id, out var act))
        {
            return;
        }

        logger.LogInformation("Action finished: {Id} {Name} status={Status}", act.Id, act.Name, act.Status);
        act.CancellationTokenSource.Dispose();
        OnChanged();
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public class RunningAction
    {
        private string _status = string.Empty;
        private int _progressValue;
        private int _progressMax;
        private int _terminal;

        public event EventHandler? Changed;

        public Guid Id { get; set; }
        public string Name { get; set; }

        public string Status
        {
            get => Volatile.Read(ref _status);
            set
            {
                Volatile.Write(ref _status, value);
                OnChanged();
            }
        }

        public int ProgressValue
        {
            get => Volatile.Read(ref _progressValue);
            private set => Volatile.Write(ref _progressValue, value);
        }

        public int ProgressMax
        {
            get => Volatile.Read(ref _progressMax);
            set
            {
                Volatile.Write(ref _progressMax, value);
                OnChanged();
            }
        }

        public CancellationTokenSource CancellationTokenSource { get; set; }
        public ActionHolder Holder { get; internal set; }

        public void Cancel()
        {
            if (Interlocked.CompareExchange(ref _terminal, 1, 0) != 0)
            {
                return;
            }

            Status = "Отменено";
            Holder.Cancel(Id);
        }

        public void ProgressPlus()
        {
            IncrementProgress();
        }

        public void Finish(string? finalStatus = null)
        {
            if (Interlocked.CompareExchange(ref _terminal, 1, 0) != 0)
            {
                return;
            }

            Status = finalStatus ?? "Выполнено";
            Holder.Remove(Id);
        }

        internal void IncrementProgress()
        {
            Interlocked.Increment(ref _progressValue);
            OnChanged();
        }

        private void OnChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
