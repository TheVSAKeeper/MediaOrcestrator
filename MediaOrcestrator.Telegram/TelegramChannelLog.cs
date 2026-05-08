using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Telegram;

internal static partial class TelegramChannelLog
{
    [LoggerMessage(EventId = 3100, Level = LogLevel.Information, Message = "Получение списка видео из Telegram-канала")]
    public static partial void ListingMedia(this ILogger logger);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information, Message = "Получение видео {ExternalId} из Telegram")]
    public static partial void GettingMedia(
        this ILogger logger,
        string externalId);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Information, Message = "Скачивание видео {VideoId} из Telegram")]
    public static partial void StartingDownload(
        this ILogger logger,
        string videoId);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Information, Message = "Видео сохранено: {Path} ({Size} байт)")]
    public static partial void MediaDownloaded(
        this ILogger logger,
        string path,
        long size);

    [LoggerMessage(EventId = 3110, Level = LogLevel.Information, Message = "Загрузка видео в Telegram-канал. Название: '{Title}'")]
    public static partial void StartingUpload(
        this ILogger logger,
        string title);

    [LoggerMessage(EventId = 3111, Level = LogLevel.Information, Message = "Видео загружено в Telegram. Message ID: {ExternalId}")]
    public static partial void MediaUploaded(
        this ILogger logger,
        string externalId);

    [LoggerMessage(EventId = 3112, Level = LogLevel.Error, Message = "Ошибка загрузки видео в Telegram")]
    public static partial void UploadFailed(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(EventId = 3120, Level = LogLevel.Information, Message = "Обновление видео {ExternalId} в Telegram")]
    public static partial void StartingUpdate(
        this ILogger logger,
        string externalId);

    [LoggerMessage(EventId = 3121, Level = LogLevel.Error, Message = "Ошибка обновления видео {ExternalId} в Telegram")]
    public static partial void UpdateFailed(
        this ILogger logger,
        string externalId,
        Exception exception);

    [LoggerMessage(EventId = 3130, Level = LogLevel.Information, Message = "Удаление видео {ExternalId} из Telegram")]
    public static partial void StartingDelete(
        this ILogger logger,
        string externalId);

    [LoggerMessage(EventId = 3131, Level = LogLevel.Information, Message = "Видео {ExternalId} удалено из Telegram")]
    public static partial void MediaDeleted(
        this ILogger logger,
        string externalId);

    [LoggerMessage(EventId = 3140, Level = LogLevel.Information, Message = "Telegram: авторизация успешна. Пользователь: {Name} (ID: {UserId})")]
    public static partial void AuthSucceeded(
        this ILogger logger,
        string name,
        long userId);

    [LoggerMessage(EventId = 3150, Level = LogLevel.Warning, Message = "ffprobe не найден, видео будет загружено без метаданных")]
    public static partial void FfprobeNotFound(this ILogger logger);

    [LoggerMessage(EventId = 3151, Level = LogLevel.Warning, Message = "ffprobe завершился с кодом {ExitCode} для файла: {FilePath}")]
    public static partial void FfprobeExited(
        this ILogger logger,
        int exitCode,
        string filePath);

    [LoggerMessage(EventId = 3152, Level = LogLevel.Warning, Message = "Не удалось получить информацию о видео через ffprobe: {FilePath}")]
    public static partial void FfprobeFailed(
        this ILogger logger,
        string filePath,
        Exception exception);
}
