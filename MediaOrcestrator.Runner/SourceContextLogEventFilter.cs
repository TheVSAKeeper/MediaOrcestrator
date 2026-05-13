using Serilog.Events;

namespace MediaOrcestrator.Runner;

public sealed class SourceContextLogEventFilter
{
    private volatile FilterState _state = FilterState.Empty;

    public void SetFilter(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            _state = FilterState.Empty;
            return;
        }

        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var includes = new List<string>();
        var excludes = new List<string>();

        foreach (var part in parts)
        {
            if (part.StartsWith('!'))
            {
                var rest = part[1..].Trim();
                if (rest.Length > 0)
                {
                    excludes.Add(rest);
                }
            }
            else
            {
                includes.Add(part);
            }
        }

        _state = new(includes.Count == 0 ? null : includes.ToArray(),
            excludes.Count == 0 ? null : excludes.ToArray());
    }

    public bool IsEnabled(LogEvent logEvent)
    {
        var state = _state;

        if (state.Includes == null && state.Excludes == null)
        {
            return true;
        }

        if (!logEvent.Properties.TryGetValue("SourceContext", out var value)
            || value is not ScalarValue { Value: string sourceContext })
        {
            return state.Includes == null;
        }

        if (state.Excludes != null)
        {
            foreach (var exclude in state.Excludes)
            {
                if (sourceContext.Contains(exclude, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        if (state.Includes == null)
        {
            return true;
        }

        foreach (var include in state.Includes)
        {
            if (sourceContext.Contains(include, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record FilterState(string[]? Includes, string[]? Excludes)
    {
        public static readonly FilterState Empty = new(null, null);
    }
}
