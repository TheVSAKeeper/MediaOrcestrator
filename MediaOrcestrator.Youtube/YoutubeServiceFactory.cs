using Google.Apis.Auth.OAuth2;
using Google.Apis.YouTube.v3;

namespace MediaOrcestrator.Youtube;

internal interface IYoutubeServiceFactory
{
    YouTubeService Create(
        UserCredential credential,
        string applicationName);
}

internal sealed class YoutubeServiceFactory : IYoutubeServiceFactory
{
    public const string ApiClientName = "Youtube.Api";

    public YouTubeService Create(
        UserCredential credential,
        string applicationName)
    {
        return new(new()
        {
            HttpClientInitializer = credential,
            ApplicationName = applicationName,
        });
    }
}
