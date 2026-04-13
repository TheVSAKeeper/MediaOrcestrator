using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Youtube;

public sealed class YoutubeAuthService(ILogger logger)
{
    private static readonly string[] Scopes =
    [
        YouTubeService.Scope.YoutubeUpload,
        YouTubeService.Scope.Youtube,
    ];

    private UserCredential? _cachedCredential;
    private string? _cachedSettingsKey;

    public static bool IsConfigured(Dictionary<string, string> settings)
    {
        return !string.IsNullOrEmpty(settings.GetValueOrDefault("client_id"))
               && !string.IsNullOrEmpty(settings.GetValueOrDefault("client_secret"))
               && !string.IsNullOrEmpty(settings.GetValueOrDefault("_system_state_path"));
    }

    public static bool HasCachedToken(Dictionary<string, string> settings)
    {
        if (!IsConfigured(settings))
        {
            return false;
        }

        var tokenDir = GetTokenDir(settings);

        return Directory.Exists(tokenDir)
               && Directory.EnumerateFiles(tokenDir, "Google.Apis.Auth.OAuth2.Responses.*").Any();
    }

    public async Task<YouTubeService> CreateServiceAsync(Dictionary<string, string> settings, CancellationToken cancellationToken)
    {
        var clientId = settings.GetValueOrDefault("client_id")
                       ?? throw new InvalidOperationException("Настройка 'client_id' не задана. Укажите OAuth Client ID из Google Cloud Console.");

        var clientSecret = settings.GetValueOrDefault("client_secret")
                           ?? throw new InvalidOperationException("Настройка 'client_secret' не задана. Укажите OAuth Client Secret из Google Cloud Console.");

        var tokenDir = GetTokenDir(settings);
        Directory.CreateDirectory(tokenDir);

        var settingsKey = $"{clientId}:{tokenDir}";

        UserCredential credential;

        if (_cachedCredential is not null && _cachedSettingsKey == settingsKey && !_cachedCredential.Token.IsStale)
        {
            credential = _cachedCredential;
        }
        else
        {
            logger.LogDebug("Авторизация YouTube API. Токен: {TokenDir}", tokenDir);

            var clientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
            };

            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(clientSecrets,
                Scopes,
                "user",
                cancellationToken,
                new FileDataStore(tokenDir, true));

            if (credential.Token.IsStale)
            {
                logger.LogInformation("OAuth-токен устарел, обновляю...");
                var refreshed = await credential.RefreshTokenAsync(cancellationToken);

                if (!refreshed)
                {
                    throw new InvalidOperationException("Не удалось обновить OAuth-токен. Удалите файл токена и авторизуйтесь заново.");
                }

                logger.LogInformation("OAuth-токен успешно обновлён");
            }

            _cachedCredential = credential;
            _cachedSettingsKey = settingsKey;
        }

        return new(new()
        {
            HttpClientInitializer = credential,
            ApplicationName = settings.GetValueOrDefault("app_name", "MediaOrcestrator"),
        });
    }

    private static string GetTokenDir(Dictionary<string, string> settings)
    {
        return Path.Combine(settings["_system_state_path"], "oauth");
    }
}
