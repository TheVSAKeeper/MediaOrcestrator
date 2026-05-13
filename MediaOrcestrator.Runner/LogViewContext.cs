using Serilog.Core;
using Serilog.Sinks.RichTextBoxForms;

namespace MediaOrcestrator.Runner;

public sealed record LogViewContext(
    RichTextBox Control,
    RichTextBoxSinkOptions SinkOptions,
    LoggingLevelSwitch LevelSwitch,
    SourceContextLogEventFilter SourceFilter,
    BufferingLogSink BufferingSink);
