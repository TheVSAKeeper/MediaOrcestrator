using LiteDB;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MediaOrcestrator.Domain;

public class Orcestrator(
    PluginManager pluginManager,
    LiteDatabase db,
    TempManager tempManager,
    StateManager stateManager,
    ActionHolder actionHolder,
    ILogger<Orcestrator> logger)
{
    private readonly object _syncCoordLock = new();
    private readonly HashSet<string> _runningSourceIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _bulkSyncRunning;

    // TODO: единая блокировка записи в БД на время синхронизации; при росте объёма – батч-запись после параллельного сбора
    private readonly object _dbWriteLock = new();

    private long _transferPlanRevision;

    public long TransferPlanRevision => Interlocked.Read(ref _transferPlanRevision);

    private void MarkTransferPlanChanged() => Interlocked.Increment(ref _transferPlanRevision);

    public Dictionary<string, ISourceType> GetSourceTypes()
    {
        return pluginManager.MediaSources;
    }

    public void Init()
    {
        pluginManager.Init();
        var sources = GetSourceTypes();
        var migrated = MediaLinkBsonMigrator.BackfillMissingFields(db, _dbWriteLock);
        if (migrated > 0)
        {
            logger.LogInformation("Миграция MediaLink: проставлены Title/Description из родителя для {Count} медиа", migrated);
        }
    }

    public async Task GetStorageFullInfo(bool isFull, Source? filterSource = null, bool onlyNew = false, IProgress<string>? progress = null, IReadOnlyDictionary<string, IProgress<string>>? sourceProgress = null, CancellationToken cancellationToken = default)
    {
        if (!TryEnterSync(filterSource))
        {
            if (filterSource == null)
            {
                logger.LogWarning("Полная синхронизация заблокирована: уже выполняется bulk или есть активные per-source");
                progress?.Report("Синхронизация уже идёт");
            }
            else
            {
                logger.LogWarning("Синхронизация источника {Source} заблокирована: bulk или этот же источник уже выполняются", filterSource.TitleFull);
                progress?.Report($"«{filterSource.TitleFull}» уже синхронизируется");
            }

            return;
        }

        try
        {
            logger.LogInformation("Запуск процесса синхронизации {Source}...", filterSource?.TitleFull);
            progress?.Report(filterSource != null ? $"Запуск синхронизации «{filterSource.TitleFull}»" : "Запуск полной синхронизации");

            var mediaCol = db.GetCollection<Media>("medias");
            //mediaCol.DeleteAll();
            var mediaAll = mediaCol.FindAll().ToList();

            var cache = new MediaSourceCache();

            foreach (var media in mediaAll)
            {
                foreach (var source in media.Sources)
                {
                    if (filterSource != null)
                    {
                        if (source.SourceId != filterSource.Id)
                        {
                            continue;
                        }
                    }

                    source.Media = media;
                    cache.GetMedia(source.SourceId).Add(source);
                }
            }

            logger.LogInformation("Обнаружено {Count} элементов медиа в локальном кэше.", mediaAll.Count);
            progress?.Report($"Загружено {mediaAll.Count} медиа из кэша");

            var sourceTypes = GetSourceTypes();
            var sources = GetSources();

            sources = filterSource != null
                ? sources.Where(x => x.Id == filterSource.Id).ToList()
                : sources.Where(x => !x.IsDisable).ToList();

            var parentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var syncModeText = onlyNew ? "Только новые" : isFull ? "Полная синхронизация" : "Обновление";

            ActionHolder.RunningAction? parentAction = filterSource != null
                ? null
                : actionHolder.Register("Синхронизация источников",
                    $"0 / {sources.Count} источников",
                    sources.Count,
                    parentCts,
                    syncModeText,
                    ActionKind.Sync);

            if (filterSource == null && sources.Count == 0)
            {
                logger.LogInformation("Активных источников для синхронизации нет");
                progress?.Report("Нет активных источников");
                parentAction?.Finish("Нет активных источников");
                return;
            }

            var failedSources = new ConcurrentBag<string>();
            var syncCancelled = false;

            try
            {
                await Parallel.ForEachAsync(sources, async (mediaSource, cancellationToken) =>
                {
                    var ctx = CancellationTokenSource.CreateLinkedTokenSource(parentCts.Token);
                    var syncMode = onlyNew ? "Только новые" : isFull ? "Полная синхронизация" : "Обновление";
                    var lastSync = mediaSource.LastSyncedAt == null
                        ? "не синхронизировано"
                        : $"синхр.: {mediaSource.LastSyncedAt.Value.ToLocalTime():dd.MM.yyyy HH:mm}";

                    var subtitle = $"{mediaSource.TypeId} · {lastSync}";
                    var action = actionHolder.Register("Синхронизация: " + mediaSource.TitleFull, syncMode, 0, ctx, subtitle, ActionKind.Sync, parentAction);

                    IProgress<string>? perSource = null;
                    sourceProgress?.TryGetValue(mediaSource.Id, out perSource);
                    perSource?.Report("Старт…");

                    try
                    {
                        var plugin = sourceTypes.Values.FirstOrDefault(x => x.Name == mediaSource.TypeId);

                        if (plugin == null)
                        {
                            logger.LogError("Плагин для типа {TypeId} не найден.", mediaSource.TypeId);
                            progress?.Report($"Плагин для {mediaSource.TypeId} не найден");
                            perSource?.Report($"Плагин «{mediaSource.TypeId}» не найден");
                            failedSources.Add(mediaSource.TitleFull);
                            action.Fail($"Плагин «{mediaSource.TypeId}» не найден");
                            return;
                        }

                        progress?.Report($"Сбор медиа из «{mediaSource.TitleFull}»...");
                        perSource?.Report("Сбор медиа…");
                        var syncMedia = plugin.GetMedia(mediaSource.Settings, isFull, ctx.Token);
                        var mediaList = new List<MediaDto>();
                        var foundIds = new List<string>();
                        await foreach (var s in syncMedia)
                        {
                            foundIds.Add(s.Id);
                            if (foundIds.Count % 25 == 0)
                            {
                                progress?.Report($"«{mediaSource.TitleFull}»: получено {foundIds.Count}");
                                perSource?.Report($"Получено {foundIds.Count}");
                            }

                            if (foundIds.Count % 10 == 0)
                            {
                                action.Status = $"Сбор: получено {foundIds.Count}";
                                action.SetProgress(foundIds.Count);
                            }

                            if (s.IsExcluded)
                            {
                                continue;
                            }

                            var foundMediaSource = cache.GetMedia(mediaSource.Id).FirstOrDefault(x => x.ExternalId == s.Id);
                            if (foundMediaSource == null)
                            {
                                mediaList.Insert(0, s);
                                continue;
                            }

                            if (onlyNew)
                            {
                                logger.LogInformation(s.Title + " уже существует. синхронизация остановлена");
                                // получили только свежие, обновлять ничего не будем
                                break;
                            }

                            var hasChange = false;
                            if (s.Metadata is { Count: > 0 } && foundMediaSource.Media != null)
                            {
                                var providedKeys = new HashSet<string>();
                                foreach (var item in s.Metadata)
                                {
                                    providedKeys.Add(item.Key);
                                    var existing = foundMediaSource.Media.Metadata
                                        .FirstOrDefault(m => m.Key == item.Key && m.SourceId == mediaSource.Id);

                                    if (existing != null)
                                    {
                                        existing.Value = item.Value;
                                        existing.DisplayName = item.DisplayName;
                                        existing.DisplayType = item.DisplayType;
                                    }
                                    else
                                    {
                                        item.SourceId = mediaSource.Id;
                                        foundMediaSource.Media.Metadata.Add(item);
                                    }
                                }

                                foundMediaSource.Media.Metadata
                                    .RemoveAll(m => m.SourceId == mediaSource.Id && !providedKeys.Contains(m.Key));

                                hasChange = true;
                            }

                            if (foundMediaSource.Media is { } ownerMedia && ownerMedia.Sources.Count == 1)
                            {
                                if (ownerMedia.Title != s.Title)
                                {
                                    ownerMedia.Title = s.Title;
                                    hasChange = true;
                                }

                                if (ownerMedia.Description != s.Description)
                                {
                                    ownerMedia.Description = s.Description;
                                    hasChange = true;
                                }
                            }

                            if (foundMediaSource.Title != s.Title)
                            {
                                foundMediaSource.Title = s.Title;
                                hasChange = true;
                            }

                            if (foundMediaSource.Description != s.Description)
                            {
                                foundMediaSource.Description = s.Description;
                                hasChange = true;
                            }

                            if (foundMediaSource.Status is MediaStatus.Missing or MediaStatus.Error)
                            {
                                foundMediaSource.Status = MediaStatus.Ok;
                                hasChange = true;
                            }

                            if (hasChange && foundMediaSource.Media is { } changedMedia)
                            {
                                lock (_dbWriteLock)
                                {
                                    UpdateMedia(changedMedia);
                                }
                            }
                        }

                        action.Status = $"Сбор: получено {foundIds.Count}";
                        action.SetProgress(foundIds.Count);

                        if (mediaList.Count > 0)
                        {
                            action.ProgressMax = mediaList.Count;
                            action.SetProgress(0);
                        }

                        var sortNumber = cache.GetMedia(mediaSource.Id).Select(x => x.SortNumber).DefaultIfEmpty(1).Max();
                        var saved = 0;
                        foreach (var s in mediaList)
                        {
                            var mediaId = Guid.NewGuid().ToString();
                            foreach (var item in s.Metadata ?? [])
                            {
                                item.SourceId = mediaSource.Id;
                            }

                            var myMedia = new Media
                            {
                                Title = s.Title,
                                Id = mediaId,
                                Description = s.Description,
                                Metadata = s.Metadata ?? [],
                                Sources = [],
                            };

                            var newMediaSource = new MediaSourceLink
                            {
                                MediaId = mediaId,
                                Media = myMedia,
                                ExternalId = s.Id,
                                Description = s.Description,
                                Title = s.Title,
                                Status = MediaStatus.Ok,
                                SourceId = mediaSource.Id,
                                SortNumber = sortNumber,
                            };

                            sortNumber++;

                            myMedia.Sources.Add(newMediaSource);

                            // поправить циклический зависимость
                            lock (_dbWriteLock)
                            {
                                mediaCol.Insert(myMedia);
                                MarkTransferPlanChanged();
                            }

                            cache.GetMedia(mediaSource.Id).Add(newMediaSource);

                            saved++;
                            action.SetProgress(saved);
                            action.Status = $"Сохранение {saved} / {mediaList.Count}";

                            if (saved == 1 || saved == mediaList.Count || saved % 10 == 0)
                            {
                                perSource?.Report($"Сохранение {saved} / {mediaList.Count}");
                            }
                        }

                        if (!onlyNew)
                        {
                            if (foundIds.Count > 0)
                            {
                                var existsVideos = cache.GetMedia(mediaSource.Id);
                                foreach (var existsVideo in existsVideos)
                                {
                                    if (existsVideo.Status == MediaStatus.Skipped || string.IsNullOrEmpty(existsVideo.ExternalId))
                                    {
                                        continue;
                                    }

                                    if (!foundIds.Contains(existsVideo.ExternalId))
                                    {
                                        existsVideo.Status = MediaStatus.Missing;

                                        lock (_dbWriteLock)
                                        {
                                            UpdateMedia(existsVideo.Media);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                logger.LogWarning("Пропуск пометки «пропало» для источника {Source}: список полученных элементов пуст", mediaSource.TitleFull);
                            }
                        }

                        var sourcesCol = db.GetCollection<Source>("sources");

                        lock (_dbWriteLock)
                        {
                            var dbSource = sourcesCol.FindById(mediaSource.Id);

                            if (dbSource != null)
                            {
                                dbSource.LastSyncedAt = DateTime.UtcNow;
                                sourcesCol.Update(dbSource);
                            }
                        }

                        perSource?.Report("Готово");
                        action.Finish("Готово");
                    }
                    catch (OperationCanceledException) when (ctx.IsCancellationRequested)
                    {
                        perSource?.Report("Отменено");
                        action.MarkCancelled();
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(exception, "Ошибка синхронизации источника {Source}", mediaSource.TitleFull);
                        progress?.Report($"Ошибка «{mediaSource.TitleFull}»: {exception.Message}");
                        perSource?.Report($"Ошибка: {exception.Message}");
                        failedSources.Add(mediaSource.TitleFull);
                        action.Fail("Ошибка: " + exception.Message, exception);
                    }
                    finally
                    {
                        if (parentAction != null)
                        {
                            parentAction.ProgressPlus();
                            parentAction.Status = $"{parentAction.ProgressValue} / {sources.Count} источников";
                        }
                    }
                });

                logger.LogInformation("Синхронизация успешно завершена.");
                progress?.Report("Синхронизация завершена");
            }
            catch (OperationCanceledException) when (parentCts.IsCancellationRequested)
            {
                syncCancelled = true;
                throw;
            }
            finally
            {
                if (parentAction != null)
                {
                    if (syncCancelled || parentCts.IsCancellationRequested)
                    {
                        parentAction.MarkCancelled($"Отменено: {parentAction.ProgressValue} из {sources.Count} источников");
                    }
                    else if (!failedSources.IsEmpty)
                    {
                        var summary = string.Join(", ", failedSources);
                        parentAction.Fail($"Сбой источников ({failedSources.Count}): {summary}");
                    }
                    else
                    {
                        parentAction.Finish($"Готово: {parentAction.ProgressValue} из {sources.Count} источников");
                    }
                }
            }
        }
        finally
        {
            ExitSync(filterSource);
        }
    }

    private bool TryEnterSync(Source? filterSource)
    {
        lock (_syncCoordLock)
        {
            if (_bulkSyncRunning)
            {
                return false;
            }

            if (filterSource == null)
            {
                if (_runningSourceIds.Count > 0)
                {
                    return false;
                }

                _bulkSyncRunning = true;
                return true;
            }

            return _runningSourceIds.Add(filterSource.Id);
        }
    }

    private void ExitSync(Source? filterSource)
    {
        lock (_syncCoordLock)
        {
            if (filterSource == null)
            {
                _bulkSyncRunning = false;
            }
            else
            {
                _runningSourceIds.Remove(filterSource.Id);
            }
        }
    }

    public List<Media> GetMedias()
    {
        return db.GetCollection<Media>("medias").FindAll().ToList();
    }

    public void UpdateMedia(Media media)
    {
        if (db.GetCollection<Media>("medias").Update(media))
        {
            MarkTransferPlanChanged();
        }
    }

    public async Task<ChronologyPlan> BuildChronologyAsync(
        IReadOnlyCollection<string> sourceIds,
        bool backfillMissingDates = false,
        int backfillParallelism = 1,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sources = GetSources()
            .Where(s => sourceIds.Contains(s.Id))
            .ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var action = actionHolder.Register(
            backfillMissingDates ? "Хронология: построение и дополнение дат" : "Хронология: построение ленты",
            "Анализ медиа",
            0,
            cts);

        try
        {
            if (backfillMissingDates)
            {
                var dateless = GetMedias()
                    .Where(m => m.Sources.Any(s => sources.ContainsKey(s.SourceId))
                                && m.Sources
                                    .Where(s => sources.ContainsKey(s.SourceId))
                                    .All(s => ChronologyPlanner.ExtractDate(m.Metadata, s.SourceId) is null))
                    .ToList();

                action.ProgressMax = dateless.Count;
                var done = 0;
                action.Status = $"Дополняю даты: 0 из {dateless.Count}";

                await Parallel.ForEachAsync(dateless,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, backfillParallelism),
                        CancellationToken = cts.Token,
                    },
                    async (media, ct) =>
                    {
                        var link = media.Sources.First(s => sources.ContainsKey(s.SourceId));
                        progress?.Report($"Дополняю дату: {media.Title}");
                        await BackfillSourceMetadataAsync(media, link.SourceId, ct);
                        var processed = Interlocked.Increment(ref done);
                        action.SetProgress(processed);
                        action.Status = $"Дополняю даты: {processed} из {dateless.Count}";
                    });
            }

            action.Status = "Сортировка";
            var plan = ChronologyPlanner.Plan(GetMedias(), sourceIds, ResolveUrl);
            action.Finish($"Готово: {plan.Items.Count} видео");
            return plan;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            action.MarkCancelled();
            throw;
        }
        catch (Exception exception)
        {
            action.Fail("Ошибка: " + exception.Message, exception);
            throw;
        }

        string? ResolveUrl(string sourceId, string externalId, IReadOnlyList<MetadataItem>? metadata)
        {
            return sources.TryGetValue(sourceId, out var source) && source.Type is not null
                ? source.Type.GetExternalUri(externalId, source.Settings, metadata)?.ToString()
                : null;
        }
    }

    public async Task ForceUpdateMetadataAsync(Media media, CancellationToken ct = default)
    {
        logger.LogInformation("Начато принудительное обновление метаданных для {MediaTitle}", media.Title);

        media.Metadata.Clear();

        foreach (var sourceLink in media.Sources)
        {
            await UpdateSourceMetadataAsync(media, sourceLink.SourceId, ct);
        }

        SyncMediaTitleFromSingleSource(media);

        UpdateMedia(media);
        logger.LogInformation("Завершено принудительное обновление метаданных для {MediaTitle}", media.Title);
    }

    public async Task ForceUpdateMetadataAsync(Media media, string sourceId, CancellationToken ct = default)
    {
        media.Metadata.RemoveAll(m => m.SourceId == sourceId);
        await UpdateSourceMetadataAsync(media, sourceId, ct);
        SyncMediaTitleFromSingleSource(media);
        UpdateMedia(media);
        logger.LogInformation("Обновлены метаданные источника {SourceId} для {MediaTitle}", sourceId, media.Title);
    }

    private async Task BackfillSourceMetadataAsync(Media media, string sourceId, CancellationToken ct)
    {
        media.Metadata.RemoveAll(m => m.SourceId == sourceId);
        await UpdateSourceMetadataAsync(media, sourceId, ct, preferQuotaFree: true);
        SyncMediaTitleFromSingleSource(media);
        UpdateMedia(media);
    }

    public void ClearSourceMetadata(Media media, string sourceId)
    {
        var removedCount = media.Metadata.RemoveAll(m => m.SourceId == sourceId);
        if (removedCount <= 0)
        {
            return;
        }

        UpdateMedia(media);
        logger.LogInformation("Очищено {Count} метаданных для источника {SourceId} в медиа {MediaTitle}", removedCount, sourceId, media.Title);
    }

    public void ClearAllMetadata(Media media)
    {
        if (media.Metadata.Count <= 0)
        {
            return;
        }

        var count = media.Metadata.Count;
        media.Metadata.Clear();
        UpdateMedia(media);
        logger.LogInformation("Очищено {Count} метаданных для медиа {MediaTitle}", count, media.Title);
    }

    public void SetSourceSkipped(Media media, string sourceId)
    {
        var link = media.Sources.FirstOrDefault(x => x.SourceId == sourceId);

        if (link == null)
        {
            link = new()
            {
                MediaId = media.Id,
                Media = media,
                SourceId = sourceId,
                ExternalId = string.Empty,
                Title = media.Title,
                Description = media.Description ?? string.Empty,
                Status = MediaStatus.Skipped,
                SortNumber = 0,
            };

            media.Sources.Add(link);
        }
        else
        {
            link.Status = MediaStatus.Skipped;
            link.StatusMessage = null;
        }

        UpdateMedia(media);
        logger.LogInformation("Медиа '{MediaTitle}' помечено как пропускаемое для источника {SourceId}", media.Title, sourceId);
    }

    public void UnsetSourceSkipped(Media media, string sourceId)
    {
        var link = media.Sources.FirstOrDefault(x => x.SourceId == sourceId);

        if (link is not { Status: MediaStatus.Skipped })
        {
            return;
        }

        if (string.IsNullOrEmpty(link.ExternalId))
        {
            media.Sources.Remove(link);
        }
        else
        {
            link.Status = MediaStatus.Ok;
            link.StatusMessage = null;
        }

        UpdateMedia(media);
        logger.LogInformation("С медиа '{MediaTitle}' снята пометка пропуска для источника {SourceId}", media.Title, sourceId);
    }

    public void RemoveMedia(Media media)
    {
        if (db.GetCollection<Media>("medias").Delete(media.Id))
        {
            MarkTransferPlanChanged();
        }
    }

    public void MergeMedia(Media target, IReadOnlyList<Media> toRemove)
    {
        var collection = db.GetCollection<Media>("medias");
        db.BeginTrans();

        try
        {
            collection.Update(target);

            foreach (var media in toRemove)
            {
                collection.Delete(media.Id);
            }

            db.Commit();
            MarkTransferPlanChanged();
        }
        catch
        {
            db.Rollback();
            throw;
        }
    }

    public List<Source> GetSources()
    {
        var sources = db.GetCollection<Source>("sources").FindAll().ToList();
        var sourceTypes = GetSourceTypes();
        foreach (var source in sources)
        {
            var sourceType = sourceTypes.Values.FirstOrDefault(x => x.Name == source.TypeId);
            if (sourceType == null)
            {
                source.IsDisable = true;
                continue;
            }

            source.Type = sourceType;
            PopulateRuntimeSettings(source);
        }

        return sources;
    }

    private void PopulateRuntimeSettings(Source source)
    {
        source.Settings["_system_temp_path"] = tempManager.TempPath;
        if (source.Type is IAuthenticatable or ISourceAuthenticationProbe)
        {
            source.Settings["_system_state_path"] = stateManager.GetSourceStatePath(source.Id);
        }
    }

    public void AddSource(string sourceId, string typeId, Dictionary<string, string> settings)
    {
        db.GetCollection<Source>("sources")
            .Insert(new Source
            {
                Id = sourceId,
                TypeId = typeId,
                Settings = settings,
            });
        MarkTransferPlanChanged();
    }

    public void RemoveSource(string sourceId)
    {
        lock (_dbWriteLock)
        {
            if (db.GetCollection<Source>("sources").Delete(sourceId))
            {
                MarkTransferPlanChanged();
            }
            InvalidateAuthentication(sourceId);
        }

        // TODO: По идее нужно, но для отладки неудобно
        //stateManager.CleanSource(sourceId);
    }

    public void UpdateSource(Source source)
    {
        lock (_dbWriteLock)
        {
            if (db.GetCollection<Source>("sources").Update(source))
            {
                MarkTransferPlanChanged();
            }
        }
    }

    public void InvalidateAuthentication(string sourceId)
    {
        lock (_dbWriteLock)
        {
            db.GetCollection<SourceAuthenticationStamp>("source_authentication").Delete(sourceId);
        }
    }

    internal SourceAuthenticationStamp? ReadAuthenticationStamp(string sourceId) =>
        db.GetCollection<SourceAuthenticationStamp>("source_authentication").FindById(sourceId);

    internal void ResetAuthenticationConfirmation(string sourceId)
    {
        lock (_dbWriteLock)
        {
            var stamps = db.GetCollection<SourceAuthenticationStamp>("source_authentication");
            if (stamps.FindById(sourceId) is { } stamp)
            {
                stamp.Revision = string.Empty;
                stamps.Update(stamp);
            }
        }
    }

    internal void SaveAuthenticationStamp(SourceAuthenticationStamp stamp)
    {
        lock (_dbWriteLock)
        {
            if (db.GetCollection<Source>("sources").Exists(source => source.Id == stamp.Id))
            {
                db.GetCollection<SourceAuthenticationStamp>("source_authentication").Upsert(stamp);
            }
        }
    }

    internal void SaveAuthenticationSettings(Source source, Dictionary<string, string> original, Dictionary<string, string> updated)
    {
        var changedKeys = original.Keys.Union(updated.Keys, original.Comparer)
            .Where(key => !key.StartsWith("_system_", StringComparison.OrdinalIgnoreCase)
                          && original.GetValueOrDefault(key) != updated.GetValueOrDefault(key))
            .ToArray();

        lock (_dbWriteLock)
        {
            var collection = db.GetCollection<Source>("sources");
            var current = collection.FindById(source.Id);
            if (current is null || current.TypeId != source.TypeId
                || changedKeys.Any(key => current.Settings.GetValueOrDefault(key) != original.GetValueOrDefault(key)))
            {
                throw new InvalidOperationException("Настройки источника изменились во время входа.");
            }

            foreach (var key in changedKeys)
            {
                if (updated.TryGetValue(key, out var value))
                {
                    current.Settings[key] = value;
                }
                else
                {
                    current.Settings.Remove(key);
                }
            }

            if (changedKeys.Length > 0 && !collection.Update(current))
            {
                throw new InvalidOperationException("Не удалось сохранить данные входа.");
            }

            source.Settings = current.Settings;
            PopulateRuntimeSettings(source);
            ResetAuthenticationConfirmation(source.Id);
        }
    }

    public List<SourceSyncRelation> GetRelations()
    {
        //db.GetCollection<SourceSyncRelation>("source_relations")
        //    .DeleteMany(x => true);

        var relations = db.GetCollection<SourceSyncRelation>("source_relations").FindAll().ToList();
        var sourceTypes = GetSourceTypes();
        var sources = GetSources();
        foreach (var item in relations)
        {
            //item.From.Type = sourceTypes.Values.First(x => x.Name == item.From.TypeId);
            var fromSource = sources.FirstOrDefault(x => x.Id == item.FromId);
            var toSource = sources.FirstOrDefault(x => x.Id == item.ToId);

            if (fromSource == null || toSource == null || fromSource.IsDisable || toSource.IsDisable)
            {
                item.IsDisable = true;
            }

            if (fromSource != null)
            {
                item.From = fromSource;
            }

            if (toSource != null)
            {
                item.To = toSource;
            }
        }

        return relations;
    }

    public void AddRelation(Source from, Source to)
    {
        AddRelation(from.Id, to.Id);
    }

    public void AddRelation(string fromId, string toId)
    {
        db.GetCollection<SourceSyncRelation>("source_relations")
            .Insert(new SourceSyncRelation
            {
                FromId = fromId,
                ToId = toId,
            });
        MarkTransferPlanChanged();
    }

    public void RemoveRelation(Source from, Source to)
    {
        RemoveRelation(from.Id, to.Id);
    }

    public void RemoveRelation(string fromId, string toId)
    {
        var removed = db.GetCollection<SourceSyncRelation>("source_relations")
            .DeleteMany(x => x.FromId == fromId && x.ToId == toId);
        if (removed > 0)
        {
            MarkTransferPlanChanged();
        }
    }

    public void InvertRelation(string fromId, string toId)
    {
        db.BeginTrans();

        try
        {
            var collection = db.GetCollection<SourceSyncRelation>("source_relations");
            collection.DeleteMany(x => x.FromId == fromId && x.ToId == toId);
            collection.Insert(new SourceSyncRelation
            {
                FromId = toId,
                ToId = fromId,
            });

            db.Commit();
            MarkTransferPlanChanged();
        }
        catch
        {
            db.Rollback();
            throw;
        }
    }

    public int ClearCollection(string collectionName)
    {
        logger.LogInformation("Очистка коллекции: {CollectionName}", collectionName);
        var collection = db.GetCollection(collectionName);
        var count = collection.DeleteAll();
        if (count > 0 && collectionName.ToLowerInvariant() is "medias" or "source_relations" or "sources")
        {
            MarkTransferPlanChanged();
        }

        logger.LogInformation("[✓] Коллекция {CollectionName} очищена. Удалено записей: {Count}", collectionName, count);
        return count;
    }

    public void ClearDatabase()
    {
        logger.LogInformation("========== Начало очистки базы данных ==========");

        var mediaCount = ClearCollection("medias");
        logger.LogInformation("[✓] Коллекция медиа очищена. Удалено записей: {MediaCount}", mediaCount);

        var relationCount = ClearCollection("source_relations");
        logger.LogInformation("[✓] Коллекция связей очищена. Удалено записей: {RelationCount}", relationCount);

        var sourceCount = ClearCollection("sources");
        logger.LogInformation("[✓] Коллекция источников очищена. Удалено записей: {SourceCount}", sourceCount);

        logger.LogInformation("========== Очистка БД завершена успешно ==========");
        logger.LogInformation("Всего удалено записей: {TotalCount} (медиа: {MediaCount}, связи: {RelationCount}, источники: {SourceCount})",
            mediaCount + relationCount + sourceCount, mediaCount, relationCount, sourceCount);
    }

    // TODO: Сомнительно
    public async Task<Media> PublishMediaAsync(
        Source source,
        string title,
        string? description,
        string videoFilePath,
        string? coverFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(videoFilePath);

        if (!File.Exists(videoFilePath))
        {
            throw new FileNotFoundException("Файл видео не найден", videoFilePath);
        }

        if (!string.IsNullOrEmpty(coverFilePath) && !File.Exists(coverFilePath))
        {
            throw new FileNotFoundException("Файл обложки не найден", coverFilePath);
        }

        if (source.Type == null)
        {
            throw new InvalidOperationException($"Источник «{source.TitleFull}» не инициализирован (Type == null).");
        }

        var mediaId = Guid.NewGuid().ToString();
        var tempDir = Path.Combine(tempManager.TempPath, mediaId);
        Directory.CreateDirectory(tempDir);

        logger.LogInformation("Публикация «{Title}» в источник {Source}. MediaId: {MediaId}", title, source.TitleFull, mediaId);

        var videoExt = Path.GetExtension(videoFilePath);
        var tempVideoPath = Path.Combine(tempDir, "media" + (string.IsNullOrEmpty(videoExt) ? ".mp4" : videoExt));
        File.Copy(videoFilePath, tempVideoPath, true);

        string? tempCoverPath = null;
        if (!string.IsNullOrEmpty(coverFilePath))
        {
            var coverExt = Path.GetExtension(coverFilePath);
            tempCoverPath = Path.Combine(tempDir, "cover" + (string.IsNullOrEmpty(coverExt) ? ".jpg" : coverExt));
            File.Copy(coverFilePath, tempCoverPath, true);
        }

        var dto = new MediaDto
        {
            Id = mediaId,
            Title = title,
            Description = description ?? string.Empty,
            TempDataPath = tempVideoPath,
        };

        if (tempCoverPath != null)
        {
            dto.TempPreviewPath = tempCoverPath;
        }

        UploadResult uploadResult;
        try
        {
            uploadResult = await UploadWithActionAsync(source, dto, title, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            tempManager.CleanMedia(mediaId);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ошибка при публикации «{Title}» в {Source}", title, source.TitleFull);
            tempManager.CleanMedia(mediaId);
            throw;
        }

        if (string.IsNullOrEmpty(uploadResult.Id))
        {
            tempManager.CleanMedia(mediaId);
            throw new InvalidOperationException($"Публикация в {source.TitleFull} не вернула идентификатор: {uploadResult.Status.Text} {uploadResult.Message}");
        }

        var mediaCollection = db.GetCollection<Media>("medias");
        var existingLinks = mediaCollection.FindAll()
            .SelectMany(x => x.Sources ?? [])
            .Where(x => x.SourceId == source.Id)
            .ToList();

        var sortNumber = existingLinks.Select(x => x.SortNumber).DefaultIfEmpty(0).Max() + 1;

        var media = new Media
        {
            Id = mediaId,
            Title = title,
            Description = description ?? string.Empty,
            Sources = [],
        };

        var link = new MediaSourceLink
        {
            MediaId = mediaId,
            Media = media,
            SourceId = source.Id,
            ExternalId = uploadResult.Id!,
            Status = uploadResult.Status.Id,
            SortNumber = sortNumber,
        };

        media.Sources.Add(link);
        mediaCollection.Insert(media);
        MarkTransferPlanChanged();

        tempManager.CleanMedia(mediaId);

        try
        {
            await ForceUpdateMetadataAsync(media, source.Id, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Не удалось обновить метаданные после публикации «{Title}» в {Source}", title, source.TitleFull);
        }

        logger.LogInformation("Публикация «{Title}» завершена. ExternalId: {ExternalId}", title, uploadResult.Id);
        return media;
    }

    public async Task TransferByRelation(Media media, SourceSyncRelation rel, CancellationToken cancellationToken = default)
    {
        var fromMediaSource = media.Sources.FirstOrDefault(x => x.SourceId == rel.From.Id);
        if (fromMediaSource == null)
        {
            throw new($"MediaSourceLink не найден для {rel.From.Id}");
        }

        if (fromMediaSource.Status != MediaStatus.Ok)
        {
            throw new($"MediaSourceLink для {rel.From.Id} в плохом статусе " + fromMediaSource.Status);
        }

        var toMediaSource = media.Sources.FirstOrDefault(x => x.SourceId == rel.To.Id);
        // todo частично успешные пока повторно не обрабатываем
        if (toMediaSource != null && toMediaSource.Status == MediaStatus.Ok)
        {
            // фаил уже загружен, значит ничего не делаем
            // в будущем возможно обновлять будем
            logger.LogInformation("Пропущено синхронизирование медиа {Media} в {ToSource} / уже имеется", media, rel.To);
            return;
        }

        var persistedToLink = db.GetCollection<Media>("medias")
            .FindById(media.Id)
            ?.Sources?
            .FirstOrDefault(x => x.SourceId == rel.To.Id);

        if (persistedToLink is { Status: MediaStatus.Ok })
        {
            if (toMediaSource == null)
            {
                toMediaSource = new()
                {
                    MediaId = media.Id,
                    Media = media,
                    SourceId = rel.To.Id,
                };

                media.Sources.Add(toMediaSource);
            }

            toMediaSource.Title = persistedToLink.Title;
            toMediaSource.Description = persistedToLink.Description;
            toMediaSource.Status = persistedToLink.Status;
            toMediaSource.StatusMessage = persistedToLink.StatusMessage;
            toMediaSource.ExternalId = persistedToLink.ExternalId;
            toMediaSource.SortNumber = persistedToLink.SortNumber;

            logger.LogWarning("Предотвращён двойной залив (#55): медиа {Media} уже загружено в {ToSource} (ExternalId {ExternalId}) по данным БД, переданный объект устарел",
                media, rel.To, persistedToLink.ExternalId);

            return;
        }

        var transferCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var transferAction = actionHolder.Register($"Передача: «{media.Title}» ({rel.From.TitleFull} → {rel.To.TitleFull})",
            "Передача...",
            0,
            transferCts,
            kind: ActionKind.Transfer);

        try
        {
            using (actionHolder.BeginScope(transferAction))
            {
                var token = transferCts.Token;

                UploadResult uploadResult;
                MediaDto? tempMedia = null;
                var debug = false;
                if (debug)
                {
                    if (DateTime.Now.Second % 10 < 5)
                    {
                        throw new("ошибка");
                    }

                    uploadResult = new()
                    {
                        Status = MediaStatusHelper.Ok(),
                        Id = Guid.NewGuid().ToString(),
                    };
                }
                else
                {
                    tempMedia = await DownloadWithActionAsync(rel.From, fromMediaSource.ExternalId, media.Title, token);
                    tempMedia.Id = media.Id;
                    try
                    {
                        if (toMediaSource?.Status == MediaStatus.PartialOk)
                        {
                            var externalId = toMediaSource.ExternalId;
                            uploadResult = await rel.To.Type.UpdateAsync(externalId, tempMedia, CurrentSourceSettings(rel.To), token);
                        }
                        else
                        {
                            uploadResult = await UploadWithActionAsync(rel.To, tempMedia, media.Title, token);
                        }
                    }
                    catch (SourceSessionRejectedException exception)
                    {
                        throw new SourceOperationAuthenticationException(rel.To, exception);
                    }
                    catch (Exception exception) when (exception is OperationCanceledException or SourceOperationAuthenticationException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // TODO: Слегка жидкая тема
                        logger.LogError(ex, "Загрузка медиа {Media} в {ToSource} завершилась ошибкой, проставляется статус Error", media, rel.To);
                        MarkUploadFailure(media, rel.To.Id, ex.Message);
                        throw;
                    }
                }

                // если айди есть, значит частично или полностью успех и связь устанавливаем
                if (uploadResult.Id != null)
                {
                    if (toMediaSource == null)
                    {
                        toMediaSource = new()
                        {
                            MediaId = media.Id,
                            Media = media,
                            SourceId = rel.To.Id,
                        };

                        media.Sources.Add(toMediaSource);
                    }

                    toMediaSource.Title = media.Title;
                    toMediaSource.Description = media.Description;
                    toMediaSource.Status = uploadResult.Status.Id;
                    toMediaSource.ExternalId = uploadResult.Id!;
                    UpdateMedia(media);
                    logger.LogInformation("Успешно синхронизировано медиа {Media} в {ToSource}. ExternalId: {ExternalId}", media, rel.To, uploadResult.Id);

                    if (tempMedia != null && !string.IsNullOrEmpty(tempMedia.TempDataPath))
                    {
                        var guid = Path.GetFileName(Path.GetDirectoryName(tempMedia.TempDataPath));

                        if (guid != null)
                        {
                            tempManager.CleanMedia(guid);
                        }
                    }

                    try
                    {
                        await ForceUpdateMetadataAsync(media, rel.From.Id, token);
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Не удалось обновить метаданные источника {FromSource} после переноса медиа {Media}", rel.From, media);
                    }

                    try
                    {
                        await ForceUpdateMetadataAsync(media, rel.To.Id, token);
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Не удалось обновить метаданные после переноса медиа {Media} в {ToSource}", media, rel.To);
                    }
                }
                else
                {
                    throw new("Провал синхронизации: " + uploadResult.Status.Text + " " + uploadResult.Message);
                }
            }

            transferAction.Finish("Передано");
        }
        catch (MediaDownloadSkippedException exception)
        {
            transferAction.Finish("Пропущено: " + exception.Message);
            logger.LogInformation("Передача медиа {Media} из {FromSource} в {ToSource} пропущена: {Reason}",
                media, rel.From, rel.To, exception.Message);
            throw;
        }
        catch (OperationCanceledException)
        {
            transferAction.MarkCancelled();
            throw;
        }
        catch (Exception exception)
        {
            transferAction.Fail("Ошибка: " + exception.Message, exception);
            throw;
        }
    }

    public async Task DeleteMediaFromSourceAsync(Media media, Source source, CancellationToken cancellationToken = default)
    {
        var sourceLink = media.Sources.FirstOrDefault(s => s.SourceId == source.Id);
        if (sourceLink == null)
        {
            logger.LogWarning("Попытка удалить медиа {MediaId} из источника {SourceId}, но связь отсутствует", media.Id, source.Id);
            throw new InvalidOperationException($"Медиа {media.Title} отсутствует в источнике {source.TitleFull}");
        }

        logger.LogInformation("Начало удаления медиа {MediaId} ({MediaTitle}) из источника {SourceId} ({SourceTitle})",
            media.Id, media.Title, source.Id, source.TitleFull);

        try
        {
            await source.Type.DeleteAsync(sourceLink.ExternalId, source.Settings, cancellationToken);
            logger.LogInformation("Успешно удалено медиа {MediaId} из источника {SourceId}", media.Id, source.Id);
        }
        catch (NotSupportedException exception)
        {
            logger.LogError(exception, "Тип источника {SourceType} не поддерживает удаление", source.TypeId);
            throw new InvalidOperationException($"Источник {source.TitleFull} не поддерживает удаление", exception);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ошибка при удалении медиа {MediaId} из источника {SourceId}", media.Id, source.Id);
            throw;
        }

        CleanupMediaLinks(media, source.Id);
    }

    // TODO: Присутсвует дублирование с UploadWithActionAsync
    private async Task<MediaDto> DownloadWithActionAsync(Source source, string externalId, string mediaTitle, CancellationToken cancellationToken)
    {
        var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var downloadAction = actionHolder.Register($"Загрузка: «{mediaTitle}» ({source.TitleFull})",
            "Подготовка...",
            100,
            downloadCts,
            kind: ActionKind.Download);

        try
        {
            var eta = new ProgressEtaEstimator();
            var ticker = new SubtitleEtaTicker(downloadAction, eta);
            var lastPercent = -1;
            var downloadProgress = new Progress<DownloadProgress>(p =>
            {
                var percent = (int)Math.Clamp(p.Percent, 0, 100);
                ticker.Report(p.Percent);

                if (percent == lastPercent)
                {
                    return;
                }

                lastPercent = percent;
                downloadAction.SetProgress(percent);
                downloadAction.Status = $"Загрузка {percent}%";
            });

            var result = await source.Type!.DownloadAsync(externalId, CurrentSourceSettings(source), downloadProgress, downloadCts.Token);
            downloadAction.Subtitle = string.Empty;
            downloadAction.Finish("Загружено");
            return result;
        }
        catch (MediaDownloadSkippedException exception)
        {
            downloadAction.Subtitle = string.Empty;
            downloadAction.Finish("Пропущено: " + exception.Message);
            throw;
        }
        catch (SourceSessionRejectedException exception)
        {
            var error = new SourceOperationAuthenticationException(source, exception);
            downloadAction.Subtitle = string.Empty;
            downloadAction.Fail(error.Message, error);
            throw error;
        }
        catch (OperationCanceledException) when (downloadCts.IsCancellationRequested)
        {
            downloadAction.Subtitle = string.Empty;
            downloadAction.MarkCancelled();
            throw;
        }
        catch (Exception exception)
        {
            downloadAction.Subtitle = string.Empty;
            downloadAction.Fail("Ошибка: " + exception.Message, exception);
            throw;
        }
    }

    private async Task<UploadResult> UploadWithActionAsync(Source target, MediaDto dto, string mediaTitle, CancellationToken cancellationToken)
    {
        var uploadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var uploadAction = actionHolder.Register($"Заливка: «{mediaTitle}» ({target.TitleFull})",
            "Подготовка...",
            100,
            uploadCts,
            kind: ActionKind.Upload);

        try
        {
            var eta = new ProgressEtaEstimator();
            var ticker = new SubtitleEtaTicker(uploadAction, eta);
            var lastPercent = -1;
            var uploadProgress = new Progress<UploadProgress>(p =>
            {
                var percent = (int)Math.Clamp(p.Percent, 0, 100);
                ticker.Report(p.Percent);

                if (percent == lastPercent)
                {
                    return;
                }

                lastPercent = percent;
                uploadAction.SetProgress(percent);
                uploadAction.Status = $"Заливка {percent}%";
            });

            var result = await target.Type!.UploadAsync(dto, CurrentSourceSettings(target), uploadProgress, uploadCts.Token);
            uploadAction.Subtitle = string.Empty;

            if (result.Id is null)
            {
                uploadAction.Fail($"Ошибка: {result.Status.Text} {result.Message}");
            }
            else
            {
                uploadAction.Finish("Залито");
            }

            return result;
        }
        catch (SourceSessionRejectedException exception)
        {
            var error = new SourceOperationAuthenticationException(target, exception);
            uploadAction.Subtitle = string.Empty;
            uploadAction.Fail(error.Message, error);
            throw error;
        }
        catch (OperationCanceledException) when (uploadCts.IsCancellationRequested)
        {
            uploadAction.Subtitle = string.Empty;
            uploadAction.MarkCancelled();
            throw;
        }
        catch (Exception exception)
        {
            uploadAction.Subtitle = string.Empty;
            uploadAction.Fail("Ошибка: " + exception.Message, exception);
            throw;
        }
    }

    private Dictionary<string, string> CurrentSourceSettings(Source source)
    {
        if (source.Type is not IAuthenticatable || db.GetCollection<Source>("sources").FindById(source.Id) is not { } current
            || current.TypeId != source.TypeId)
        {
            return source.Settings;
        }

        current.Type = source.Type;
        PopulateRuntimeSettings(current);
        return current.Settings;
    }

    private async Task UpdateSourceMetadataAsync(Media media, string sourceId, CancellationToken ct, bool preferQuotaFree = false)
    {
        var source = GetSources().FirstOrDefault(s => s.Id == sourceId);
        if (source == null || source.IsDisable)
        {
            return;
        }

        var plugin = GetSourceTypes().Values.FirstOrDefault(x => x.Name == source.TypeId);
        if (plugin == null)
        {
            return;
        }

        var sourceLink = media.Sources.FirstOrDefault(s => s.SourceId == sourceId);
        if (sourceLink == null)
        {
            return;
        }

        try
        {
            var dto = preferQuotaFree && plugin is ISupportsQuotaFreeMetadata quotaFree
                ? await quotaFree.GetMediaByIdQuotaFreeAsync(sourceLink.ExternalId, source.Settings, ct)
                : await plugin.GetMediaByIdAsync(sourceLink.ExternalId, source.Settings, ct);

            if (dto is null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(dto.Title))
            {
                sourceLink.Title = dto.Title;
            }

            if (!string.IsNullOrEmpty(dto.Description))
            {
                sourceLink.Description = dto.Description;
            }

            if (dto.Metadata is { Count: > 0 })
            {
                foreach (var item in dto.Metadata)
                {
                    item.SourceId = source.Id;
                    media.Metadata.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при обновлении метаданных из источника {SourceTitle}", source.TitleFull);
        }
    }

    private void SyncMediaTitleFromSingleSource(Media media)
    {
        if (media.Sources.Count != 1)
        {
            return;
        }

        var sourceTitle = media.Sources[0].Title;

        if (string.IsNullOrEmpty(sourceTitle) || media.Title == sourceTitle)
        {
            return;
        }

        logger.LogInformation("Название медиа обновлено: «{OldTitle}» → «{NewTitle}»", media.Title, sourceTitle);
        media.Title = sourceTitle;
    }

    private void MarkUploadFailure(Media media, string toSourceId, string errorMessage)
    {
        var link = media.Sources.FirstOrDefault(x => x.SourceId == toSourceId);
        if (link == null)
        {
            link = new()
            {
                MediaId = media.Id,
                Media = media,
                SourceId = toSourceId,
                ExternalId = string.Empty,
                Title = string.Empty,
                Description = string.Empty,
            };

            media.Sources.Add(link);
        }

        link.Status = MediaStatus.Error;
        UpdateMedia(media);
        logger.LogInformation("Для медиа {MediaId}/{ToSourceId} проставлен статус Error: {Message}", media.Id, toSourceId, errorMessage);
    }

    private void CleanupMediaLinks(Media media, string sourceId)
    {
        logger.LogInformation("Очистка записей в базе данных для медиа {MediaId}, источник {SourceId}", media.Id, sourceId);

        var linkToRemove = media.Sources.FirstOrDefault(s => s.SourceId == sourceId);
        if (linkToRemove != null)
        {
            media.Sources.Remove(linkToRemove);
            logger.LogDebug("Удалена связь MediaSourceLink для источника {SourceId}", sourceId);
        }

        var removedMetadataCount = media.Metadata.RemoveAll(m => m.SourceId == sourceId);
        if (removedMetadataCount > 0)
        {
            logger.LogDebug("Удалено {RemovedCount} элементов метаданных для источника {SourceId}", removedMetadataCount, sourceId);
        }

        if (media.Sources.Count != 0)
        {
            UpdateMedia(media);
            logger.LogInformation("Медиа {MediaId} обновлено в базе данных (осталось источников: {RemainingCount})", media.Id, media.Sources.Count);
        }
        else
        {
            RemoveMedia(media);
            logger.LogInformation("Медиа {MediaId} удалено из базы данных (не осталось источников)", media.Id);
        }
    }

    public class MediaSourceCache
    {
        private readonly ConcurrentDictionary<string, List<MediaSourceLink>> _holder = new();

        public List<MediaSourceLink> GetMedia(string sourceId) => _holder.GetOrAdd(sourceId, _ => []);
    }
}
