namespace MediaOrcestrator.HardDiskDrive;

public sealed class HardDiskDriveOptions
{
    public TimeSpan ThumbnailDownloadTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan PooledConnectionIdleTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
