namespace MediaOrcestrator.Runner.Wpf.Services;

internal sealed class PreloadCompletionSignal
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completion => _tcs.Task;

    public void Complete() => _tcs.TrySetResult();
}
