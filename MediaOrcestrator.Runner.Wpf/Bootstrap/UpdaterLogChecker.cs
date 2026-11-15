using Serilog;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

internal static class UpdaterLogChecker
{
    private const int MaxSummaryLength = 220;
    private const string ErrorMarker = "ОШИБКА";
    private const string RollbackMarker = "Откат завершён.";

    public static UpdaterLogCheck Check()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "updater.log");

        if (!File.Exists(logPath))
        {
            return new(UpdaterLogResult.Absent);
        }

        try
        {
            var content = File.ReadAllText(logPath);

            if (content.Contains(ErrorMarker, StringComparison.Ordinal))
            {
                var summary = ExtractErrorSummary(content);
                var rolledBack = content.Contains(RollbackMarker, StringComparison.Ordinal);

                Log.Warning("Обнаружены ошибки в updater.log:\n{Content}", content);

                try
                {
                    File.Delete(logPath);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Не удалось удалить updater.log после чтения");
                }

                return new(UpdaterLogResult.HasErrors, summary, rolledBack);
            }

            Log.Information("Обновление прошло успешно (updater.log)");
            File.Delete(logPath);
            return new(UpdaterLogResult.Ok);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Не удалось прочитать updater.log");
            return new(UpdaterLogResult.ReadFailed);
        }
    }

    private static string? ExtractErrorSummary(string content)
    {
        var lines = content
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains(ErrorMarker, StringComparison.Ordinal))
            .Select(CleanErrorLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        if (lines.Length == 0)
        {
            return null;
        }

        var summary = string.Join('\n', lines);

        return summary.Length <= MaxSummaryLength
            ? summary
            : string.Concat(summary.AsSpan(0, MaxSummaryLength - 1), "…");
    }

    private static string CleanErrorLine(string line)
    {
        var text = line.AsSpan().Trim();

        if (text.StartsWith('['))
        {
            var closing = text.IndexOf(']');

            if (closing >= 0)
            {
                text = text[(closing + 1)..].TrimStart();
            }
        }

        if (text.StartsWith("ОШИБКА ОТКАТА:", StringComparison.Ordinal))
        {
            var body = text["ОШИБКА ОТКАТА:".Length..].Trim();
            return body.Length > 0 ? $"Откат: {body}" : "Откат не удался";
        }

        if (text.StartsWith(ErrorMarker, StringComparison.Ordinal))
        {
            text = text[ErrorMarker.Length..].TrimStart(':').TrimStart();
        }

        return text.ToString();
    }
}

internal readonly record struct UpdaterLogCheck(
    UpdaterLogResult Status,
    string? ErrorSummary = null,
    bool RolledBack = false)
{
    public bool HasErrors => Status == UpdaterLogResult.HasErrors;
}

internal enum UpdaterLogResult
{
    None = 0,
    Absent = 1,
    Ok = 2,
    HasErrors = 3,
    ReadFailed = 4,
}
