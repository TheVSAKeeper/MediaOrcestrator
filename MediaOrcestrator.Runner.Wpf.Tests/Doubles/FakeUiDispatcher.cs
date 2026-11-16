using KeepShell.Services.Platform;

namespace MediaOrcestrator.Runner.Wpf.Tests.Doubles;

public sealed class FakeUiDispatcher : IUiDispatcher
{
    private readonly List<FakeUiTimer> _timers = [];

    public bool HasAccess { get; set; } = true;

    public IReadOnlyList<FakeUiTimer> Timers => _timers;

    public int InvokeCount { get; private set; }

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        InvokeCount++;
        action();
    }

    public IUiTimer CreateTimer(TimeSpan interval, Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        var timer = new FakeUiTimer(interval, tick);
        _timers.Add(timer);
        return timer;
    }

    public void FireAllRunningTimers()
    {
        foreach (var timer in _timers.ToArray())
        {
            if (timer.IsRunning)
            {
                timer.FireTick();
            }
        }
    }

    public sealed class FakeUiTimer(TimeSpan interval, Action tick) : IUiTimer
    {
        public bool IsRunning { get; private set; }

        public TimeSpan Interval { get; set; } = interval;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            IsRunning = true;
            StartCount++;
        }

        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            StopCount++;
        }

        public void FireTick()
        {
            if (!IsRunning)
            {
                return;
            }

            tick();
        }
    }
}
