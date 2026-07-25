using MediaOrcestrator.Modules;
using System.Text.Json;

namespace MediaOrcestrator.Youtube;

internal sealed class YoutubeQuotaTracker
{
    public const int DailyLimit = 10_000;
    public const int UploadCost = 1600;
    public const int WriteCost = 50;

    private const string FileName = "quota.json";

    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

    private readonly object _gate = new();

    public QuotaSnapshot? Read(string? statePath)
    {
        if (string.IsNullOrEmpty(statePath))
        {
            return null;
        }

        var state = ApplyRollover(Load(statePath), TodayPt());
        return new(state.UsedUnits, DailyLimit, state.ExhaustedUntil);
    }

    public void Record(string? statePath, int units)
    {
        if (string.IsNullOrEmpty(statePath) || units <= 0)
        {
            return;
        }

        lock (_gate)
        {
            var state = ApplyRollover(Load(statePath), TodayPt());
            Save(statePath, state with { UsedUnits = state.UsedUnits + units });
        }
    }

    public void MarkExhausted(string? statePath)
    {
        if (string.IsNullOrEmpty(statePath))
        {
            return;
        }

        lock (_gate)
        {
            var state = ApplyRollover(Load(statePath), TodayPt());
            Save(statePath, state with { ExhaustedUntil = NextMidnightPt() });
        }
    }

    private static QuotaState ApplyRollover(QuotaState state, string todayPt)
    {
        return state.QuotaDayPt == todayPt ? state : new(todayPt, 0, null);
    }

    private static string TodayPt()
    {
        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Pacific).ToString("yyyy-MM-dd");
    }

    private static DateTimeOffset NextMidnightPt()
    {
        var nowPt = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Pacific);
        return new(nowPt.Date.AddDays(1), nowPt.Offset);
    }

    private static QuotaState Load(string statePath)
    {
        var path = Path.Combine(statePath, FileName);

        if (!File.Exists(path))
        {
            return new(TodayPt(), 0, null);
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), YoutubeJsonContext.Default.QuotaState)
                   ?? new(TodayPt(), 0, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new(TodayPt(), 0, null);
        }
    }

    private static void Save(string statePath, QuotaState state)
    {
        Directory.CreateDirectory(statePath);
        File.WriteAllText(Path.Combine(statePath, FileName),
            JsonSerializer.Serialize(state, YoutubeJsonContext.Default.QuotaState));
    }
}

internal sealed record QuotaState(string QuotaDayPt, int UsedUnits, DateTimeOffset? ExhaustedUntil);
