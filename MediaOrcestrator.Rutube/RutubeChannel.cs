using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace MediaOrcestrator.Rutube;

// TODO: Костыль с ILogger<RutubeService>. Желательно сделать полноценную регистрацию модулей в DI.
public class RutubeChannel(ILogger<RutubeChannel> logger, ILogger<RutubeService> serviceLogger) : ISourceType
{
    public SyncDirection ChannelType => SyncDirection.OnlyUpload;

    public string Name => "Rutube";

    public IEnumerable<SourceSettings> SettingsKeys { get; } =
    [
        new()
        {
            Key = "auth_state_path",
            IsRequired = true,
            Title = "путь до фаила куки",
        },
        new()
        {
            Key = "category_id",
            IsRequired = true,
            Title = "идентификатор категории",
        },
    ];

    public MediaDto[] GetMedia()
    {
        throw new NotImplementedException();
    }

    public async IAsyncEnumerable<MediaDto> GetMedia(Dictionary<string, string> settings)
    {
        logger.LogWarning("Получение списка медиа из RuTube не реализовано");
        await Task.CompletedTask;
        yield break;
    }

    public MediaDto GetMediaById()
    {
        throw new NotImplementedException();
    }

    public Task<MediaDto> Download(string videoId, Dictionary<string, string> settings)
    {
        logger.LogWarning("Загрузка видео с RuTube не реализована. ID: {VideoId}", videoId);
        throw new NotImplementedException("Загрузка с RuTube не поддерживается");
    }

    public async Task<string> Upload(MediaDto media, Dictionary<string, string> settings)
    {
        logger.LogInformation("Начало загрузки видео на RuTube. Название: '{Title}'", media.Title);

        var filePath = media.TempDataPath;
        if (!File.Exists(filePath))
        {
            logger.LogError("Файл видео не найден: {FilePath}", filePath);
            throw new FileNotFoundException("Файл видео не найден", filePath);
        }

        logger.LogDebug("Файл найден. Размер: {FileSize} байт", new FileInfo(filePath).Length);

        var authStatePath = settings["auth_state_path"];
        var rutubeCategoryId = settings["category_id"];

        if (!File.Exists(authStatePath))
        {
            logger.LogError("Файл аутентификации не найден: {AuthStatePath}", authStatePath);
            throw new FileNotFoundException("Файл auth_state.json не найден. Необходимо выполнить авторизацию", authStatePath);
        }

        logger.LogDebug("Чтение данных аутентификации из: {AuthStatePath}", authStatePath);

        var authStateBody = await File.ReadAllTextAsync(authStatePath);
        using var authState = JsonDocument.Parse(authStateBody);
        var cookies = authState.RootElement.GetProperty("cookies");

        var cookieStringBuilder = new StringBuilder();
        string? csrfToken = null;

        foreach (var cookie in cookies.EnumerateArray())
        {
            var name = cookie.GetProperty("name").GetString()!;
            var value = cookie.GetProperty("value").GetString()!;
            var domain = cookie.GetProperty("domain").GetString()!;

            if (domain.Contains("rutube.ru") || domain.Contains("studio.rutube.ru") || domain.Contains("gid.ru"))
            {
                cookieStringBuilder.Append($"{name}={value}; ");
            }

            if (name == "csrftoken" && domain == "studio.rutube.ru")
            {
                csrfToken = value;
            }
        }

        if (string.IsNullOrEmpty(csrfToken))
        {
            logger.LogError("CSRF токен не найден в файле аутентификации");
            throw new InvalidOperationException("CSRF токен не найден в auth_state.json");
        }

        logger.LogDebug("CSRF токен успешно получен");

        var rutubeService = new RutubeService(cookieStringBuilder.ToString(), csrfToken, serviceLogger);

        try
        {
            var sessionId = await rutubeService.UploadVideoAsync(filePath, media.Title, media.Description, rutubeCategoryId);
            logger.LogInformation("Видео успешно загружено на RuTube. Session ID: {SessionId}, Название: '{Title}'", sessionId, media.Title);
            return sessionId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при загрузке видео на RuTube. Название: '{Title}'", media.Title);
            throw;
        }
    }
}

//public class RutubeMedia : Media
//{
//    public string Title { get; set; }
//    public string Description { get; set; }

//    public string Id => throw new NotImplementedException();
//}
