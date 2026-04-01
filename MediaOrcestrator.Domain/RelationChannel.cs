using System.Threading.Channels;

namespace MediaOrcestrator.Domain;

public sealed class RelationChannel(SourceSyncRelation relation)
{
    private readonly Channel<SyncIntent> _channel = Channel.CreateUnbounded<SyncIntent>();

    public SourceSyncRelation Relation => relation;

    public void Enqueue(SyncIntent intent)
    {
        _channel.Writer.TryWrite(intent);
    }

    public void Complete()
    {
        _channel.Writer.TryComplete();
    }

    public async Task RunAsync(Func<SyncIntent, Task> executeFunc, CancellationToken ct)
    {
        await foreach (var intent in _channel.Reader.ReadAllAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            await executeFunc(intent);
        }
    }
}
