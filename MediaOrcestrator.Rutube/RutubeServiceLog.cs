using Microsoft.Extensions.Logging;
using System.Net;

namespace MediaOrcestrator.Rutube;

internal static partial class RutubeServiceLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Инициализация сессии загрузки на RuTube")]
    public static partial void InitUploadSession(this ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Сессия создана. Session ID: {SessionId}, Video ID: {VideoId}")]
    public static partial void UploadSessionCreated(this ILogger logger, string sessionId, string videoId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Создание TUS ресурса для загрузки")]
    public static partial void CreatingTusResource(this ILogger logger);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "URL загрузки получен: {UploadUrl}")]
    public static partial void TusResourceCreated(this ILogger logger, string uploadUrl);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Начало загрузки видео данных")]
    public static partial void StartingDataUpload(this ILogger logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Загрузка данных завершена")]
    public static partial void DataUploadCompleted(this ILogger logger);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Debug, Message = "Ожидание обработки на сервере (5 секунд)")]
    public static partial void WaitingServerProcessing(this ILogger logger);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "Обновление метаданных видео (черновик)")]
    public static partial void UpdatingDraftMetadata(this ILogger logger);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = "Метаданные обновлены")]
    public static partial void MetadataUpdated(this ILogger logger);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = "Загрузка превью")]
    public static partial void UploadingThumbnail(this ILogger logger);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "Обнаружен WebP формат, конвертируем в JPG...")]
    public static partial void ConvertingWebPToJpg(this ILogger logger);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Information, Message = "Конвертация завершена, загружаем JPG")]
    public static partial void WebPConverted(this ILogger logger);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Error, Message = "Ошибка при конвертации WebP в JPG, пробуем загрузить оригинал")]
    public static partial void WebPConversionFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Information, Message = "Превью загружено: {ThumbnailUrl}")]
    public static partial void ThumbnailUploaded(this ILogger logger, string? thumbnailUrl);

    [LoggerMessage(EventId = 1014, Level = LogLevel.Error, Message = "Ошибка загрузки превьюшки")]
    public static partial void ThumbnailUploadFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1015, Level = LogLevel.Information, Message = "Планирование публикации на {PublishAt}")]
    public static partial void SchedulingPublication(this ILogger logger, DateTime publishAt);

    [LoggerMessage(EventId = 1016, Level = LogLevel.Information, Message = "Видео успешно запланировано")]
    public static partial void PublicationScheduled(this ILogger logger);

    [LoggerMessage(EventId = 1017, Level = LogLevel.Information, Message = "Немедленная публикация видео")]
    public static partial void PublishingNow(this ILogger logger);

    [LoggerMessage(EventId = 1018, Level = LogLevel.Information, Message = "Видео успешно опубликовано")]
    public static partial void Published(this ILogger logger);

    [LoggerMessage(EventId = 1019, Level = LogLevel.Error, Message = "Ошибка публикации")]
    public static partial void PublicationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1020, Level = LogLevel.Debug, Message = "Запрос информации о видео {VideoId}")]
    public static partial void RequestingVideoInfo(this ILogger logger, string videoId);

    [LoggerMessage(EventId = 1021, Level = LogLevel.Error, Message = "Ошибка получения видео {VideoId}. Статус: {StatusCode}, Ответ: {Response}")]
    public static partial void GetVideoFailed(this ILogger logger, string videoId, HttpStatusCode statusCode, string response);

    [LoggerMessage(EventId = 1022, Level = LogLevel.Information, Message = "Получена информация о видео. Название: '{Title}'")]
    public static partial void VideoInfoReceived(this ILogger logger, string title);

    [LoggerMessage(EventId = 1023, Level = LogLevel.Debug, Message = "Запрос списка категорий RuTube")]
    public static partial void RequestingCategories(this ILogger logger);

    [LoggerMessage(EventId = 1024, Level = LogLevel.Error, Message = "Ошибка получения категорий. Статус: {StatusCode}, Ответ: {Response}")]
    public static partial void GetCategoriesFailed(this ILogger logger, HttpStatusCode statusCode, string response);

    [LoggerMessage(EventId = 1025, Level = LogLevel.Information, Message = "Получено категорий: {Count}")]
    public static partial void CategoriesReceived(this ILogger logger, int count);

    [LoggerMessage(EventId = 1026, Level = LogLevel.Information, Message = "Отправка DELETE запроса в RuTube API для видео {VideoId}")]
    public static partial void SendingDeleteRequest(this ILogger logger, string videoId);

    [LoggerMessage(EventId = 1027, Level = LogLevel.Warning, Message = "Видео {VideoId} не найдено на RuTube, считаем уже удаленным")]
    public static partial void VideoNotFoundTreatedAsDeleted(this ILogger logger, string videoId);

    [LoggerMessage(EventId = 1028, Level = LogLevel.Error, Message = "Не удалось удалить видео {VideoId}. Статус: {StatusCode}, Ответ: {Response}")]
    public static partial void DeleteVideoFailed(this ILogger logger, string videoId, HttpStatusCode statusCode, string response);

    [LoggerMessage(EventId = 1029, Level = LogLevel.Information, Message = "Видео {VideoId} успешно удалено через RuTube API")]
    public static partial void VideoDeleted(this ILogger logger, string videoId);

    [LoggerMessage(EventId = 1030, Level = LogLevel.Debug, Message = "Загрузка превью для видео {VideoId} из {ThumbnailPath}")]
    public static partial void UploadingThumbnailFile(this ILogger logger, string videoId, string thumbnailPath);

    [LoggerMessage(EventId = 1031, Level = LogLevel.Error, Message = "Ошибка загрузки превью. Статус: {StatusCode}, Ответ: {Response}")]
    public static partial void ThumbnailUploadStatusFailed(this ILogger logger, HttpStatusCode statusCode, string response);

    [LoggerMessage(EventId = 1032, Level = LogLevel.Error, Message = "Ошибка получения видео. Статус: {StatusCode}, Ответ: {Response}")]
    public static partial void GetVideoListFailed(this ILogger logger, HttpStatusCode statusCode, string response);

    [LoggerMessage(EventId = 1033, Level = LogLevel.Error, Message = "Ошибка инициализации сессии. Статус: {StatusCode}, Ответ: {Response}")]
    public static partial void InitSessionFailed(this ILogger logger, HttpStatusCode statusCode, string response);

    [LoggerMessage(EventId = 1034, Level = LogLevel.Error, Message = "Не удалось десериализовать ответ сессии загрузки")]
    public static partial void UploadSessionDeserializationFailed(this ILogger logger);

    [LoggerMessage(EventId = 1035, Level = LogLevel.Error, Message = "Ошибка создания TUS ресурса. Статус: {StatusCode}, Ответ: {Response}")]
    public static partial void TusResourceFailed(this ILogger logger, HttpStatusCode statusCode, string response);

    [LoggerMessage(EventId = 1036, Level = LogLevel.Debug, Message = "Начало загрузки файла. Размер: {FileSize} байт")]
    public static partial void StartingFileUpload(this ILogger logger, long fileSize);

    [LoggerMessage(EventId = 1037, Level = LogLevel.Error, Message = "Ошибка TUS загрузки. Статус: {StatusCode}, Ответ: {Response}")]
    public static partial void TusUploadFailed(this ILogger logger, HttpStatusCode statusCode, string response);

    [LoggerMessage(EventId = 1038, Level = LogLevel.Information, Message = "Загрузка файла завершена. Всего загружено: {TotalBytes} байт")]
    public static partial void FileUploadCompleted(this ILogger logger, long totalBytes);

    [LoggerMessage(EventId = 1039, Level = LogLevel.Error, Message = "Ошибка обновления метаданных. Статус: {StatusCode}, Ответ: {Response}")]
    public static partial void MetadataUpdateFailed(this ILogger logger, HttpStatusCode statusCode, string response);

    [LoggerMessage(EventId = 1040, Level = LogLevel.Debug, Message = "Метаданные подтверждены. Название: '{Title}', Категория: '{Category}', Скрыто: {IsHidden}")]
    public static partial void MetadataConfirmed(this ILogger logger, string title, string category, bool isHidden);

    [LoggerMessage(EventId = 1041, Level = LogLevel.Error, Message = "Ошибка публикации видео. Статус: {StatusCode}, Ответ: {Response}")]
    public static partial void PublishVideoFailed(this ILogger logger, HttpStatusCode statusCode, string response);

    [LoggerMessage(EventId = 1042, Level = LogLevel.Debug, Message = "Публикация подтверждена. Video ID: {VideoId}, Запланировано: {Timestamp}")]
    public static partial void PublicationConfirmed(this ILogger logger, string videoId, string timestamp);
}
