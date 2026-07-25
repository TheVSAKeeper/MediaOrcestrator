namespace MediaOrcestrator.Modules;

public interface ISupportsQuotaFreeMetadata
{
    Task<MediaDto?> GetMediaByIdQuotaFreeAsync(
        string externalId,
        Dictionary<string, string> settings,
        CancellationToken cancellationToken = default);
}
