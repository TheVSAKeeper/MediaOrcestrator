using KeepShell.Bootstrap;
using Serilog;
using System.Text.RegularExpressions;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

internal sealed class WpfBindingJournal : IDisposable
{
    private const int Limit = 100;
    private const long WindowMilliseconds = 60_000;
    private static readonly Regex BindingLocation = new(
        @"BindingExpression:Path=(?<path>[\p{L}\p{N}_./()]{0,160})(?<indexed>\[)?[^;\r\n]*; DataItem=[^\r\n]*; target element is '(?<element>[\p{L}\p{N}_.+`]{1,160})' [^\r\n]*; target property is '(?<property>[\p{L}\p{N}_.]{1,100})'",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly BindingErrorSink _sink = BindingErrorSink.Attach();
    private readonly ILogger _logger;
    private readonly HashSet<BindingSite> _recent = [];
    private long _windowStart = Environment.TickCount64;
    private bool _limitReported;

    public WpfBindingJournal(ILogger logger)
    {
        _logger = logger;
        _sink.Captured += OnCaptured;
    }

    public void Dispose()
    {
        _sink.Captured -= OnCaptured;
        _sink.Dispose();
    }

    private void OnCaptured(object? sender, BindingErrorRecord error)
    {
        _sink.Clear();
        var match = BindingLocation.Match(error.Message);
        var site = new BindingSite(Describe(error.Message),
            match.Groups["path"].Value + (match.Groups["indexed"].Success ? "[…]" : ""),
            match.Groups["element"].Value, match.Groups["property"].Value);
        bool overflow;
        lock (_recent)
        {
            var now = Environment.TickCount64;
            if (now - _windowStart >= WindowMilliseconds)
            {
                _recent.Clear();
                _windowStart = now;
                _limitReported = false;
            }

            if (_recent.Contains(site))
            {
                return;
            }

            overflow = _recent.Count >= Limit;
            if (overflow)
            {
                if (_limitReported)
                {
                    return;
                }
                _limitReported = true;
            }
            else
            {
                _recent.Add(site);
            }
        }

        if (overflow)
        {
            _logger.Warning("Достигнут лимит разных ошибок привязки WPF за минуту ({Limit})", Limit);
            return;
        }

        _logger.Error("Ошибка привязки WPF ({Problem}). Путь: {BindingPath}. Элемент: {Element}.{TargetProperty}",
            site.Problem, site.Path, site.Element, site.Property);
    }

    private static string Describe(string message)
    {
        var prefix = message.AsSpan();
        if (prefix.StartsWith("BindingExpression path error", StringComparison.Ordinal))
        {
            return "путь недоступен";
        }
        if (prefix.StartsWith("Cannot find source for binding", StringComparison.Ordinal))
        {
            return "источник недоступен";
        }
        if (prefix.StartsWith("Value produced by BindingExpression is not valid", StringComparison.Ordinal))
        {
            return "недопустимое значение";
        }

        return "сбой при обновлении";
    }

    private readonly record struct BindingSite(string Problem, string Path, string Element, string Property);
}
