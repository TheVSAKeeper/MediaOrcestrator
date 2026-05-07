using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Rutube;

public sealed class RutubeServiceFactory(IHttpClientFactory httpClientFactory, ILogger<RutubeService> logger) : IRutubeServiceFactory
{
    public const string ApiClientName = "Rutube.Api";
    public const string UploadClientName = "Rutube.Upload";

    public RutubeService Create(string cookieString, string csrfToken)
    {
        var apiClient = httpClientFactory.CreateClient(ApiClientName);
        var uploadClient = httpClientFactory.CreateClient(UploadClientName);
        return new(apiClient, uploadClient, cookieString, csrfToken, logger);
    }
}
