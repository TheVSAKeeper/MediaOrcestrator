using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaOrcestrator.VkVideo;

public interface IVkVideoServiceFactory
{
    VkVideoService Create(string cookieString);
}

public sealed class VkVideoServiceFactory(
    IHttpClientFactory httpClientFactory,
    IOptions<VkVideoOptions> options,
    ILogger<VkVideoService> logger)
    : IVkVideoServiceFactory
{
    public const string ApiClientName = "VkVideo.Api";
    public const string UploadClientName = "VkVideo.Upload";

    public VkVideoService Create(string cookieString)
    {
        var apiClient = httpClientFactory.CreateClient(ApiClientName);
        var uploadClient = httpClientFactory.CreateClient(UploadClientName);
        return new(apiClient, uploadClient, cookieString, options.Value, logger);
    }
}
