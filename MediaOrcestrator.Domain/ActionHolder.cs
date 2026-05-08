using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MediaOrcestrator.Domain;

public class ActionHolder(ILogger<ActionHolder> logger)
{
    public ConcurrentDictionary<Guid, RunningAction> Actions = new();

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

        Actions.TryAdd(id, act);
        return act;
    }

    public void SetStatus(Guid id, string value)
    {
        if (Actions.TryGetValue(id, out var act))
        {
            act.Status = value;
        }
    }

    public void ProgressPlus(Guid id)
    {
        if (Actions.TryGetValue(id, out var act))
        {
            act.ProgressValue++;
        }
    }

    public void Cancel(Guid id)
    {
        if (!Actions.TryRemove(id, out var act))
        {
            return;
        }

        act.CancellationTokenSource.Cancel();
    }

    public class RunningAction
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public int ProgressValue { get; set; }
        public int ProgressMax { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }
        public ActionHolder Holder { get; internal set; }

        public void Cancel()
        {
            Status = "Отменено";
            Holder.Cancel(Id);
        }

        public void ProgressPlus()
        {
            Holder.ProgressPlus(Id);
        }

        public void Finish(string? finalStatus = null)
        {
            Status = finalStatus ?? "Выполнено";
            Holder.Cancel(Id);
        }
    }
}
