namespace MediaOrcestrator.Modules;

public sealed record QuotaSnapshot(int UsedUnits, int DailyLimit, DateTimeOffset? ExhaustedUntil);

public interface IReportsQuota
{
    QuotaSnapshot? GetQuota(Dictionary<string, string> settings);
}
