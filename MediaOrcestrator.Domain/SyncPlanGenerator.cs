using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Domain;

public class SyncPlanGenerator(Orcestrator orchestrator, ILogger<SyncPlanGenerator> logger)
{
    public Task<SyncPlan> GeneratePlanAsync()
    {
        logger.LogInformation("Начало генерации плана синхронизации...");

        var plan = new SyncPlan();

        var relations = orchestrator.GetRelations();
        var activeRelations = relations.Where(x => !x.IsDisable).ToList();

        logger.LogInformation("Найдено {RelationCount} активных связей для анализа", activeRelations.Count);

        var allIntents = new List<IntentObject>();
        
        // Iterative approach to handle chains: A -> B -> C
        bool added;
        int pass = 0;
        do
        {
            added = false;
            pass++;
            logger.LogDebug("Анализ связей, проход {Pass}", pass);
            
            foreach (var relation in activeRelations)
            {
                var relationIntents = AnalyzeRelation(relation, allIntents);
                
                foreach (var intent in relationIntents)
                {
                    if (!allIntents.Any(i => IsSameIntent(i, intent)))
                    {
                        allIntents.Add(intent);
                        added = true;
                    }
                }
            }
        } while (added && pass < 10); // Safety limit for cyclic dependencies

        plan.Intents = allIntents;

        BuildDependencies(allIntents);
        OrganizeIntents(plan, activeRelations);

        logger.LogInformation("Генерация плана синхронизации завершена. Всего уникальных намерений: {IntentCount}", plan.TotalCount);

        return Task.FromResult(plan);
    }

    private bool IsSameIntent(IntentObject a, IntentObject b)
    {
        if (a.Type != b.Type || a.MediaId != b.MediaId) return false;

        return a.Type switch
        {
            IntentType.Download => a.SourceId == b.SourceId,
            IntentType.Upload => a.TargetId == b.TargetId,
            IntentType.UpdateStatus => a.SourceId == b.SourceId,
            IntentType.MarkAsDeleted => a.SourceId == b.SourceId,
            _ => false
        };
    }

    private List<IntentObject> AnalyzeRelation(SourceSyncRelation relation, List<IntentObject> allIntents)
    {
        var intents = new List<IntentObject>();

        var allMedia = orchestrator.GetMedias();

        logger.LogDebug("Анализ связи {Relation} с {MediaCount} медиа-элементами", relation.ToString(), allMedia.Count);

        foreach (var media in allMedia)
        {
            var sourceLink = media.Sources.FirstOrDefault(s => s.SourceId == relation.FromId);
            var targetLink = media.Sources.FirstOrDefault(s => s.SourceId == relation.ToId);

            bool sourceHasIt = sourceLink != null && sourceLink.Status == MediaSourceLink.StatusOk;
            bool sourceWillHaveIt = allIntents.Any(i => i.MediaId == media.Id && i.TargetId == relation.FromId && i.Type == IntentType.Upload);
            
            bool targetHasIt = targetLink != null && targetLink.Status == MediaSourceLink.StatusOk;
            bool targetWillHaveIt = allIntents.Any(i => i.MediaId == media.Id && i.TargetId == relation.ToId && i.Type == IntentType.Upload);

            if ((sourceHasIt || sourceWillHaveIt) && !(targetHasIt || targetWillHaveIt))
            {
                logger.LogDebug("Медиа {MediaId} требует синхронизации из {From} в {To} (Доступно: {SourceAvailable}, Планируется: {SourcePlanned})",
                    media.Id, relation.From.Title, relation.To.Title, sourceHasIt, sourceWillHaveIt);

                if (sourceHasIt && sourceLink != null)
                {
                    var downloadIntent = new IntentObject
                    {
                        Type = IntentType.Download,
                        MediaId = media.Id,
                        SourceId = relation.FromId,
                        ExternalId = sourceLink.ExternalId,
                        Media = media,
                        Source = relation.From,
                    };

                    intents.Add(downloadIntent);
                }

                var uploadIntent = new IntentObject
                {
                    Type = IntentType.Upload,
                    MediaId = media.Id,
                    SourceId = relation.FromId,
                    TargetId = relation.ToId,
                    Media = media,
                    Source = relation.From,
                    Target = relation.To,
                };

                intents.Add(uploadIntent);
            }
            else if (!sourceHasIt && !sourceWillHaveIt && (targetHasIt || targetWillHaveIt))
            {
                var updateStatusIntent = new IntentObject
                {
                    Type = IntentType.UpdateStatus,
                    MediaId = media.Id,
                    SourceId = relation.FromId,
                    Media = media,
                    Source = relation.From,
                };

                intents.Add(updateStatusIntent);

                logger.LogDebug("Медиа {MediaId} отсутствует в источнике {From}, создано намерение обновления статуса", media.Id, relation.From.Title);
            }
            else if (sourceHasIt && targetHasIt)
            {
                if (sourceLink?.Status != MediaSourceLink.StatusOk)
                {
                    var updateStatusIntent = new IntentObject
                    {
                        Type = IntentType.UpdateStatus,
                        MediaId = media.Id,
                        SourceId = relation.FromId,
                        ExternalId = sourceLink?.ExternalId,
                        Media = media,
                        Source = relation.From,
                    };

                    intents.Add(updateStatusIntent);

                    logger.LogDebug("Медиа {MediaId} имеет ошибочный статус в {From}, создано намерение обновления статуса", media.Id, relation.From.Title);
                }

                if (targetLink?.Status != MediaSourceLink.StatusOk)
                {
                    var updateStatusIntent = new IntentObject
                    {
                        Type = IntentType.UpdateStatus,
                        MediaId = media.Id,
                        SourceId = relation.ToId,
                        ExternalId = targetLink?.ExternalId,
                        Media = media,
                        Source = relation.To,
                    };

                    intents.Add(updateStatusIntent);

                    logger.LogDebug("Медиа {MediaId} имеет ошибочный статус в {To}, создано намерение обновления статуса", media.Id, relation.To.Title);
                }
            }
        }

        return intents;
    }

    private void BuildDependencies(List<IntentObject> intents)
    {
        logger.LogDebug("Построение графа зависимостей для {IntentCount} намерений", intents.Count);

        var intentsByMedia = intents.GroupBy(i => i.MediaId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var mediaId in intentsByMedia.Keys)
        {
            var mediaIntents = intentsByMedia[mediaId];
            var downloadIntents = mediaIntents.Where(i => i.Type == IntentType.Download).ToList();
            var uploadIntents = mediaIntents.Where(i => i.Type == IntentType.Upload).ToList();

            foreach (var uploadIntent in uploadIntents)
            {
                // Dependency: Upload depends on Downloads for this media (must fetch data first)
                foreach (var downloadIntent in downloadIntents)
                {
                    if (!uploadIntent.Dependencies.Contains(downloadIntent))
                    {
                        uploadIntent.Dependencies.Add(downloadIntent);
                        logger.LogDebug("Добавлена зависимость: Загрузка {UploadId} зависит от скачивания {DownloadId} (Медиа {MediaId})",
                            uploadIntent.Id, downloadIntent.Id, mediaId);
                    }
                }

                // Dependency: Chain synchronization. Upload(Source -> Target) depends on Upload(Prev -> Source)
                foreach (var otherUpload in uploadIntents)
                {
                    if (otherUpload.Id == uploadIntent.Id) continue;

                    if (otherUpload.TargetId == uploadIntent.SourceId)
                    {
                        if (!uploadIntent.Dependencies.Contains(otherUpload))
                        {
                            uploadIntent.Dependencies.Add(otherUpload);
                            logger.LogDebug("Добавлена зависимость цепочки: Загрузка {UploadId} зависит от предварительной загрузки {OtherId} (Медиа {MediaId})",
                                uploadIntent.Id, otherUpload.Id, mediaId);
                        }
                    }
                }
            }
        }

        var dependencyCount = intents.Sum(i => i.Dependencies.Count);
        logger.LogInformation("Граф зависимостей построен. Всего зависимостей: {DependencyCount}", dependencyCount);
    }

    private void OrganizeIntents(SyncPlan plan, List<SourceSyncRelation> relations)
    {
        logger.LogDebug("Организация {IntentCount} намерений по связям и медиа", plan.Intents.Count);

        foreach (var relation in relations)
        {
            var relationKey = $"{relation.FromId}->{relation.ToId}";

            var relationIntents = plan.Intents.Where(i =>
                    i.SourceId == relation.FromId && i.TargetId == relation.ToId
                    || i.SourceId == relation.FromId && i.Type == IntentType.Download
                    || i.SourceId == relation.FromId && i.Type == IntentType.UpdateStatus
                    || i.SourceId == relation.ToId && i.Type == IntentType.UpdateStatus)
                .ToList();

            if (relationIntents.Count == 0)
            {
                continue;
            }

            plan.IntentsByRelation[relationKey] = relationIntents;

            logger.LogDebug("Связь {RelationKey}: {IntentCount} намерений", relationKey, relationIntents.Count);
        }

        var mediaGroups = plan.Intents.GroupBy(i => i.MediaId);
        foreach (var mediaGroup in mediaGroups)
        {
            plan.IntentsByMedia[mediaGroup.Key] = mediaGroup.ToList();
        }

        logger.LogInformation("Намерения организованы: {RelationCount} связей, {MediaCount} медиа-элементов",
            plan.IntentsByRelation.Count, plan.IntentsByMedia.Count);
    }
}
