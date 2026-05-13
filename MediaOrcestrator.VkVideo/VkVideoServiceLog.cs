using Microsoft.Extensions.Logging;
using System.Net;

namespace MediaOrcestrator.VkVideo;

internal static partial class VkVideoServiceLog
{
    [LoggerMessage(EventId = 4000, Level = LogLevel.Information, Message = "Получение списка медиа для VkVideo")]
    public static partial void ListingMedia(this ILogger logger);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "Получение списка клипов для VkVideo")]
    public static partial void ListingClips(this ILogger logger);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Debug, Message = "Запрос деталей видео: {VideoKey}")]
    public static partial void RequestingVideo(
        this ILogger logger,
        string videoKey);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Debug, Message = "Запрос каталога: {Url}")]
    public static partial void RequestingCatalog(
        this ILogger logger,
        string url);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Warning, Message = "Каталог видео пуст или не найден для группы {GroupId}")]
    public static partial void CatalogEmpty(
        this ILogger logger,
        Exception exception,
        long groupId);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Debug, Message = "Запрос первой страницы секции каталога: section={SectionId}")]
    public static partial void RequestingSectionFirstPage(
        this ILogger logger,
        string sectionId);

    [LoggerMessage(EventId = 4006, Level = LogLevel.Debug, Message = "Запрос следующей страницы каталога: section={SectionId}, from={StartFrom}")]
    public static partial void RequestingSectionNextPage(
        this ILogger logger,
        string sectionId,
        string startFrom);

    [LoggerMessage(EventId = 4007, Level = LogLevel.Debug, Message = "VK group {GroupId} screen_name={ScreenName}")]
    public static partial void GroupScreenNameResolved(
        this ILogger logger,
        long groupId,
        string screenName);

    [LoggerMessage(EventId = 4008, Level = LogLevel.Information, Message = "Запрос нового access_token через web_token")]
    public static partial void RequestingAccessToken(this ILogger logger);

    [LoggerMessage(EventId = 4009, Level = LogLevel.Information, Message = "access_token получен, истекает в {Expires}")]
    public static partial void AccessTokenReceived(
        this ILogger logger,
        DateTimeOffset expires);

    [LoggerMessage(EventId = 4010, Level = LogLevel.Warning, Message = "Ответ web_token — HTML (попытка {Attempt}/2). Статус: {StatusCode}")]
    public static partial void WebTokenHtmlResponse(
        this ILogger logger,
        int attempt,
        HttpStatusCode statusCode);

    [LoggerMessage(EventId = 4011, Level = LogLevel.Information, Message = "Повторяем запрос web_token после решения challenge...")]
    public static partial void RetryingWebTokenAfterChallenge(this ILogger logger);

    [LoggerMessage(EventId = 4012, Level = LogLevel.Error, Message = "Ошибка получения web_token. Статус: {StatusCode}, Ответ: {Body}")]
    public static partial void WebTokenFailed(
        this ILogger logger,
        HttpStatusCode statusCode,
        string body);

    [LoggerMessage(EventId = 4013, Level = LogLevel.Warning, Message = "Ответ API {Method} — HTML. Статус: {StatusCode}")]
    public static partial void ApiHtmlResponse(
        this ILogger logger,
        string method,
        HttpStatusCode statusCode);

    [LoggerMessage(EventId = 4014, Level = LogLevel.Information, Message = "Повторяем запрос API {Method} после решения challenge...")]
    public static partial void RetryingApiAfterChallenge(
        this ILogger logger,
        string method);

    [LoggerMessage(EventId = 4015, Level = LogLevel.Error, Message = "Ошибка API {Method}. Статус: {StatusCode}, Ответ: {Body}")]
    public static partial void ApiFailed(
        this ILogger logger,
        string method,
        HttpStatusCode statusCode,
        string body);

    [LoggerMessage(EventId = 4016, Level = LogLevel.Error, Message = "Ошибка VK API {Method}. Код: {ErrorCode}, Сообщение: {ErrorMsg}")]
    public static partial void VkApiError(
        this ILogger logger,
        string method,
        int errorCode,
        string errorMsg);

    [LoggerMessage(EventId = 4017, Level = LogLevel.Warning, Message = "VK API {Method}: rate limit, попытка {Attempt}/{Max}. Ждём {Delay} мс")]
    public static partial void RateLimitWait(
        this ILogger logger,
        string method,
        int attempt,
        int max,
        double delay);

    [LoggerMessage(EventId = 4018, Level = LogLevel.Debug, Message = "Ответ — HTML, но не challenge-страница")]
    public static partial void HtmlButNotChallenge(this ILogger logger);

    [LoggerMessage(EventId = 4019, Level = LogLevel.Warning, Message = "VK challenge (rate limit) обнаружен. Redirect URI: {Uri}")]
    public static partial void ChallengeDetected(
        this ILogger logger,
        Uri? uri);

    [LoggerMessage(EventId = 4020, Level = LogLevel.Error, Message = "Не удалось решить VK challenge: {Error}. URI: {Uri}")]
    public static partial void ChallengeUnsolvable(
        this ILogger logger,
        string? error,
        Uri? uri);

    [LoggerMessage(EventId = 4021, Level = LogLevel.Information, Message = "VK challenge решён: hash429={Hash429}, salt={Salt}, key={Key}")]
    public static partial void ChallengeSolved(
        this ILogger logger,
        string? hash429,
        string? salt,
        string? key);

    [LoggerMessage(EventId = 4022, Level = LogLevel.Debug, Message = "Отправка решения на {SolveUri}")]
    public static partial void ChallengeSendingSolution(
        this ILogger logger,
        Uri? solveUri);

    [LoggerMessage(EventId = 4023, Level = LogLevel.Information, Message = "Ответ на решение challenge: статус {StatusCode}, длина тела {Length}")]
    public static partial void ChallengeSolutionResponse(
        this ILogger logger,
        HttpStatusCode statusCode,
        int length);

    [LoggerMessage(EventId = 4024, Level = LogLevel.Warning, Message = "Ответ на решение challenge снова HTML. Начало: {Body}")]
    public static partial void ChallengeStillHtml(
        this ILogger logger,
        string body);

    [LoggerMessage(EventId = 4025, Level = LogLevel.Error, Message = "Ответ {Context} — HTML вместо JSON. Начало: {Body}")]
    public static partial void HtmlInsteadOfJson(
        this ILogger logger,
        string context,
        string body);

    [LoggerMessage(EventId = 4026, Level = LogLevel.Information, Message = "Шаг 1/3: Резервирование слота для '{Title}'")]
    public static partial void UploadReservingSlot(
        this ILogger logger,
        string title);

    [LoggerMessage(EventId = 4027, Level = LogLevel.Information, Message = "Слот зарезервирован. VideoId: {VideoId}, OwnerId: {OwnerId}")]
    public static partial void UploadSlotReserved(
        this ILogger logger,
        long videoId,
        long ownerId);

    [LoggerMessage(EventId = 4028, Level = LogLevel.Information, Message = "Шаг 2/3: Загрузка файла ({Size} байт)")]
    public static partial void UploadStartingFile(
        this ILogger logger,
        long size);

    [LoggerMessage(EventId = 4029, Level = LogLevel.Information, Message = "Файл загружен. Hash: {Hash}")]
    public static partial void UploadFileCompleted(
        this ILogger logger,
        string hash);

    [LoggerMessage(EventId = 4030, Level = LogLevel.Error, Message = "Ошибка загрузки файла. Статус: {StatusCode}, Ответ: {Body}")]
    public static partial void UploadFileFailed(
        this ILogger logger,
        HttpStatusCode statusCode,
        string body);

    [LoggerMessage(EventId = 4031, Level = LogLevel.Information, Message = "Загрузка превью")]
    public static partial void UploadingThumbnail(this ILogger logger);

    [LoggerMessage(EventId = 4032, Level = LogLevel.Information, Message = "Превью загружено")]
    public static partial void ThumbnailUploaded(this ILogger logger);

    [LoggerMessage(EventId = 4033, Level = LogLevel.Warning, Message = "Ошибка загрузки превью, публикация продолжится без него")]
    public static partial void ThumbnailFailedSkipping(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(EventId = 4034, Level = LogLevel.Information, Message = "Загрузка превью для shorts {OwnerId}_{VideoId}")]
    public static partial void UploadingShortsThumbnail(
        this ILogger logger,
        long ownerId,
        long videoId);

    [LoggerMessage(EventId = 4035, Level = LogLevel.Information, Message = "Превью shorts загружено. PhotoId: {PhotoId}")]
    public static partial void ShortsThumbnailUploaded(
        this ILogger logger,
        long photoId);

    [LoggerMessage(EventId = 4036, Level = LogLevel.Information, Message = "Загрузка превью для видео {OwnerId}_{VideoId}")]
    public static partial void UploadingVideoThumbnail(
        this ILogger logger,
        long ownerId,
        long videoId);

    [LoggerMessage(EventId = 4037, Level = LogLevel.Information, Message = "Превью загружено. PhotoId: {PhotoId}")]
    public static partial void VideoThumbnailUploaded(
        this ILogger logger,
        long photoId);

    [LoggerMessage(EventId = 4038, Level = LogLevel.Information, Message = "Шаг 3/3: Публикация")]
    public static partial void UploadPublishing(this ILogger logger);

    [LoggerMessage(EventId = 4039, Level = LogLevel.Information, Message = "Видео отредактировано перед публикацией")]
    public static partial void ShortVideoEditedBeforePublish(this ILogger logger);

    [LoggerMessage(EventId = 4040, Level = LogLevel.Warning, Message = "Отложенная публикация не поддерживается для shorts — видео будет опубликовано немедленно")]
    public static partial void ShortVideoNoScheduling(this ILogger logger);

    [LoggerMessage(EventId = 4041, Level = LogLevel.Information, Message = "Видео опубликовано: {Url}")]
    public static partial void UploadPublished(
        this ILogger logger,
        string? url);

    [LoggerMessage(EventId = 4042, Level = LogLevel.Information, Message = "Ожидание обработки shorts {OwnerId}_{VideoId}")]
    public static partial void WaitingShortsEncoding(
        this ILogger logger,
        long ownerId,
        long videoId);

    [LoggerMessage(EventId = 4043, Level = LogLevel.Information, Message = "Прогресс кодирования shorts {OwnerId}_{VideoId}: {Percents}%")]
    public static partial void ShortsEncodingProgress(
        this ILogger logger,
        long ownerId,
        long videoId,
        int percents);

    [LoggerMessage(EventId = 4044, Level = LogLevel.Information, Message = "Shorts {OwnerId}_{VideoId} готов к публикации ({Percents}%)")]
    public static partial void ShortsEncodingReady(
        this ILogger logger,
        long ownerId,
        long videoId,
        int percents);

    [LoggerMessage(EventId = 4045, Level = LogLevel.Information, Message = "Редактирование видео {OwnerId}_{VideoId}")]
    public static partial void EditingVideo(
        this ILogger logger,
        long ownerId,
        long videoId);

    [LoggerMessage(EventId = 4046, Level = LogLevel.Information, Message = "Видео {OwnerId}_{VideoId} успешно отредактировано")]
    public static partial void VideoEdited(
        this ILogger logger,
        long ownerId,
        long videoId);

    [LoggerMessage(EventId = 4047, Level = LogLevel.Information, Message = "Удаление видео {OwnerId}_{VideoId}")]
    public static partial void DeletingVideo(
        this ILogger logger,
        long ownerId,
        long videoId);

    [LoggerMessage(EventId = 4048, Level = LogLevel.Information, Message = "Видео {OwnerId}_{VideoId} удалено")]
    public static partial void VideoDeleted(
        this ILogger logger,
        long ownerId,
        long videoId);
}
