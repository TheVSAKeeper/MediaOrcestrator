namespace MediaOrcestrator.Domain;

public interface IReleaseProvider
{
    Task<ReleaseInfo?> GetLatestReleaseAsync(string repo, string assetPattern, CancellationToken cancellationToken = default);
    Task DownloadAssetAsync(string url, string targetPath, string? expectedDigest = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
