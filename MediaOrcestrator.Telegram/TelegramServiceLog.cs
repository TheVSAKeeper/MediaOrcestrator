using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Telegram;

internal static partial class TelegramServiceLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Подключение к Telegram")]
    public static partial void ConnectingToTelegram(this ILogger logger);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Подключено как {Name} (ID: {UserId})")]
    public static partial void ConnectedAs(
        this ILogger logger,
        string name,
        long userId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Debug, Message = "Резолв канала по числовому ID: {ChannelId}")]
    public static partial void ResolvingChannelById(
        this ILogger logger,
        long channelId);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Debug, Message = "Резолв канала по username: @{Username}")]
    public static partial void ResolvingChannelByUsername(
        this ILogger logger,
        string username);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Debug, Message = "Запрос сообщения {MessageId}")]
    public static partial void RequestingMessage(
        this ILogger logger,
        int messageId);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Warning, Message = "Сообщение {MessageId} не содержит видео")]
    public static partial void MessageHasNoVideo(
        this ILogger logger,
        int messageId);

    [LoggerMessage(EventId = 3020, Level = LogLevel.Information, Message = "Скачивание файла {DocumentId} ({Size} байт)")]
    public static partial void DownloadingFile(
        this ILogger logger,
        long documentId,
        long size);

    [LoggerMessage(EventId = 3021, Level = LogLevel.Information, Message = "Файл сохранён: {Path}")]
    public static partial void FileSaved(
        this ILogger logger,
        string path);

    [LoggerMessage(EventId = 3022, Level = LogLevel.Information, Message = "Загрузка видео в Telegram: {Path} ({Width}x{Height}, {Duration:F1}с)")]
    public static partial void UploadingVideo(
        this ILogger logger,
        string path,
        int width,
        int height,
        double duration);

    [LoggerMessage(EventId = 3023, Level = LogLevel.Information, Message = "Видео загружено. Message ID: {MessageId}")]
    public static partial void VideoUploaded(
        this ILogger logger,
        int messageId);

    [LoggerMessage(EventId = 3030, Level = LogLevel.Information, Message = "Редактирование сообщения {MessageId}")]
    public static partial void EditingMessage(
        this ILogger logger,
        int messageId);

    [LoggerMessage(EventId = 3031, Level = LogLevel.Information, Message = "Сообщение {MessageId} отредактировано")]
    public static partial void MessageEdited(
        this ILogger logger,
        int messageId);

    [LoggerMessage(EventId = 3032, Level = LogLevel.Information, Message = "Удаление сообщения {MessageId}")]
    public static partial void DeletingMessage(
        this ILogger logger,
        int messageId);

    [LoggerMessage(EventId = 3033, Level = LogLevel.Information, Message = "Сообщение {MessageId} удалено")]
    public static partial void MessageDeleted(
        this ILogger logger,
        int messageId);
}
