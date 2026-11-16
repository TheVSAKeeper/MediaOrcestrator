using MediaOrcestrator.Runner.Wpf.Services;

namespace MediaOrcestrator.Runner.Wpf.Tests.Doubles;

public sealed class NoOpPageRenderProbe : IPageRenderProbe
{
    public int IdleMeasures { get; private set; }

    public int FrameMeasures { get; private set; }

    public void MeasureIdle(Action<TimeSpan> onIdle)
    {
        ArgumentNullException.ThrowIfNull(onIdle);
        IdleMeasures++;
        onIdle(TimeSpan.Zero);
    }

    public void MeasureNextFrame(Action<long> onFrameMs)
    {
        ArgumentNullException.ThrowIfNull(onFrameMs);
        FrameMeasures++;
        onFrameMs(0);
    }
}
