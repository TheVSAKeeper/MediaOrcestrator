using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Domain;

public class ActionHolder(ILogger<ActionHolder> logger)
{
    public Dictionary<Guid, RunningAction> Actions = new Dictionary<Guid, RunningAction>();

    public Guid Register(string name, string status, int progressMax, CancellationTokenSource ctx)
    {
        var id = Guid.NewGuid();
        Actions.Add(id, new RunningAction
        {
            Name = name,
            Status = status,
            ProgressMax = progressMax,
            CancellationTokenSource = ctx,
        });
        return id;
    }

    public void SetStatus(Guid id, string value)
    {
        Actions[id].Status = value;
    }

    public void ProgressPlus(Guid id)
    {
        Actions[id].ProgressValue++;
    }

    public void Cancel(Guid id)
    {
        Actions[id].CancellationTokenSource.Cancel();
        Actions.Remove(id);
    }

    public class RunningAction
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public int ProgressValue { get; set; }
        public int ProgressMax { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }
    }
}
