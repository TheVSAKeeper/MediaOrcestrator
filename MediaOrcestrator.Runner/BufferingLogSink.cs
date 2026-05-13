using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.RichTextBoxForms;

namespace MediaOrcestrator.Runner;

public sealed class BufferingLogSink(
    int capacity,
    RichTextBoxSink inner,
    LoggingLevelSwitch levelSwitch,
    SourceContextLogEventFilter sourceFilter)
    : ILogEventSink
{
    private readonly LogEvent?[] _buffer = new LogEvent?[capacity];
    private readonly object _lock = new();
    private int _count;
    private int _writeIndex;

    public void Emit(LogEvent logEvent)
    {
        lock (_lock)
        {
            _buffer[_writeIndex] = logEvent;
            _writeIndex = (_writeIndex + 1) % _buffer.Length;
            if (_count < _buffer.Length)
            {
                _count++;
            }

            if (Passes(logEvent))
            {
                inner.Emit(logEvent);
            }
        }
    }

    public void ReapplyFilter()
    {
        lock (_lock)
        {
            inner.Clear();

            var start = _count < _buffer.Length ? 0 : _writeIndex;
            for (var i = 0; i < _count; i++)
            {
                var logEvent = _buffer[(start + i) % _buffer.Length];
                if (logEvent != null && Passes(logEvent))
                {
                    inner.Emit(logEvent);
                }
            }
        }
    }

    private bool Passes(LogEvent logEvent)
    {
        if (logEvent.Level < levelSwitch.MinimumLevel)
        {
            return false;
        }

        return sourceFilter.IsEnabled(logEvent);
    }
}
