using MediaOrcestrator.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MediaOrcestrator.HardDiskDrive;

public sealed class HardDiskDriveModule : IPluginModule
{
    public void Register(IServiceCollection services)
    {
        services.AddOptions<HardDiskDriveOptions>();

        services
            .AddHttpClient(HardDiskDriveChannel.ThumbnailClientName, ConfigureThumbnailClient)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler);

        services.AddSingleton<FfprobeMetadataReader>();
        services.AddSingleton<HardDiskDriveCodecConverter>();
        services.AddSingleton<ISourceType, HardDiskDriveChannel>();
    }

    private static void ConfigureThumbnailClient(
        IServiceProvider sp,
        HttpClient client)
    {
        var options = sp.GetRequiredService<IOptions<HardDiskDriveOptions>>().Value;
        client.Timeout = options.ThumbnailDownloadTimeout;
    }

    private static SocketsHttpHandler CreateHandler(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<HardDiskDriveOptions>>().Value;

        return new()
        {
            UseCookies = false,
            PooledConnectionLifetime = options.PooledConnectionLifetime,
            PooledConnectionIdleTimeout = options.PooledConnectionIdleTimeout,
        };
    }
}
