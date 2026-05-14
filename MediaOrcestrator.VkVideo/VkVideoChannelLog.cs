using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.VkVideo;

internal static partial class VkVideoChannelLog
{
    [LoggerMessage(EventId = 4100, Level = LogLevel.Information, Message = "Получение деталей видео {ExternalId}")]
    public static partial void RequestingVideoDetails(
        this ILogger logger,
        string externalId);

    [LoggerMessage(EventId = 4101, Level = LogLevel.Information, Message = "Скачивание видео {VideoId}")]
    public static partial void DownloadingVideo(
        this ILogger logger,
        string videoId);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Information, Message = "Скачивание видео из {Url}")]
    public static partial void DownloadingFromUrl(
        this ILogger logger,
        string url);

    [LoggerMessage(EventId = 4103, Level = LogLevel.Information, Message = "Видео сохранено: {Path} ({Size} байт)")]
    public static partial void VideoSaved(
        this ILogger logger,
        string path,
        long size);

    [LoggerMessage(EventId = 4104, Level = LogLevel.Information, Message = "Загрузка видео на VK Video. Название: '{Title}'")]
    public static partial void UploadingVideo(
        this ILogger logger,
        string title);

    [LoggerMessage(EventId = 4105, Level = LogLevel.Information, Message = "Отложенная публикация на {PublishAt}")]
    public static partial void ScheduledPublication(
        this ILogger logger,
        DateTime publishAt);

    [LoggerMessage(EventId = 4106, Level = LogLevel.Information, Message = "Видео загружено на VK Video. ID: {ExternalId}")]
    public static partial void VideoUploaded(
        this ILogger logger,
        string externalId);

    [LoggerMessage(EventId = 4107, Level = LogLevel.Error, Message = "Ошибка загрузки видео на VK Video")]
    public static partial void UploadFailed(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(EventId = 4108, Level = LogLevel.Information, Message = "Обновление видео {ExternalId} на VK Video")]
    public static partial void UpdatingVideo(
        this ILogger logger,
        string externalId);

    [LoggerMessage(EventId = 4109, Level = LogLevel.Error, Message = "Ошибка загрузки превью")]
    public static partial void ThumbnailUpdateFailed(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(EventId = 4110, Level = LogLevel.Error, Message = "Ошибка редактирования метаданных")]
    public static partial void MetadataEditFailed(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(EventId = 4111, Level = LogLevel.Information, Message = "Удаление видео {ExternalId} из VK Video")]
    public static partial void DeletingVideo(
        this ILogger logger,
        string externalId);

    [LoggerMessage(EventId = 4112, Level = LogLevel.Information, Message = "Видео {ExternalId} удалено")]
    public static partial void VideoDeleted(
        this ILogger logger,
        string externalId);

    [LoggerMessage(EventId = 4113, Level = LogLevel.Information, Message = "VK Video: авторизация сохранена в {Path}")]
    public static partial void AuthSaved(
        this ILogger logger,
        string path);

    [LoggerMessage(EventId = 4114, Level = LogLevel.Information, Message = "Превью обрезано под клип VK: {CroppedPath}")]
    public static partial void ThumbnailCropped(
        this ILogger logger,
        string croppedPath);

    [LoggerMessage(EventId = 4115, Level = LogLevel.Warning, Message = "Не удалось обрезать превью под вертикальный формат, используется оригинал")]
    public static partial void ThumbnailCropFailed(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(EventId = 4116, Level = LogLevel.Debug, Message = "Чтение данных аутентификации VK Video из: {AuthStatePath}")]
    public static partial void ReadingAuthState(
        this ILogger logger,
        string authStatePath);

    [LoggerMessage(EventId = 4117, Level = LogLevel.Information, Message = "Видео ({Duration}) длиннее лимита шортсов ({MaxDuration}) — загружаем как обычное видео")]
    public static partial void VideoTooLongForShorts(
        this ILogger logger,
        TimeSpan duration,
        TimeSpan maxDuration);
}
