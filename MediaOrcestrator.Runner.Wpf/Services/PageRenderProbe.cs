using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.Services;

public interface IPageRenderProbe
{
    void MeasureIdle(Action<TimeSpan> onIdle);

    void MeasureNextFrame(Action<long> onFrameMs);
}

public sealed class WpfPageRenderProbe : IPageRenderProbe
{
    public void MeasureIdle(Action<TimeSpan> onIdle)
    {
        ArgumentNullException.ThrowIfNull(onIdle);

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            onIdle(TimeSpan.Zero);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            stopwatch.Stop();
            onIdle(stopwatch.Elapsed);
        }));
    }

    public void MeasureNextFrame(Action<long> onFrameMs)
    {
        ArgumentNullException.ThrowIfNull(onFrameMs);

        if (Application.Current?.Dispatcher is null)
        {
            onFrameMs(0);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        EventHandler? handler = null;

        handler = (_, _) =>
        {
            CompositionTarget.Rendering -= handler;
            stopwatch.Stop();
            onFrameMs(stopwatch.ElapsedMilliseconds);
        };

        CompositionTarget.Rendering += handler;
    }
}
