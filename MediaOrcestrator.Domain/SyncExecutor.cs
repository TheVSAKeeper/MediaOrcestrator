using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Domain;

public class SyncExecutor(Orcestrator orchestrator, ILogger<SyncExecutor> logger)
{
    private CancellationTokenSource? _cancellationTokenSource;

    public event EventHandler<IntentProgressEventArgs>? ProgressChanged;

    public async Task ExecuteAsync(SyncPlan plan, IProgress<ExecutionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Начало выполнения плана синхронизации с {IntentCount} намерениями", plan.TotalCount);
        logger.LogInformation("[АУДИТ] Выполнение синхронизации начато: PlanId={PlanId}, TotalIntents={TotalIntents}",
            plan.Id, plan.TotalCount);

        // Create internal cancellation token source that can be cancelled via Cancel() method
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            // Validate selected intents
            var selectedIntents = ValidateSelectedIntents(plan);
            if (selectedIntents.Count == 0)
            {
                logger.LogWarning("Не выбрано намерений для выполнения");
                logger.LogInformation("[АУДИТ] Выполнение синхронизации прервано: Причина=НамеренияНеВыбраны");
                return;
            }

            logger.LogInformation("Проверено {SelectedCount} выбранных намерений", selectedIntents.Count);

            // Perform topological sort by dependencies
            var sortedIntents = TopologicalSort(selectedIntents);
            logger.LogInformation("Отсортировано {IntentCount} намерений по зависимостям", sortedIntents.Count);

            // Execute intents in dependency order
            var completedCount = 0;
            var failedCount = 0;
            var skippedCount = 0;
            var cancelledByUser = false;

            foreach (var intent in sortedIntents)
            {
                if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                {
                    logger.LogInformation("Выполнение отменено пользователем");
                    cancelledByUser = true;
                    break;
                }

                // Check if dependencies are satisfied
                if (!AreDependenciesSatisfied(intent))
                {
                    intent.Status = IntentStatus.Skipped;
                    skippedCount++;
                    logger.LogWarning("Пропуск намерения {IntentId} из-за невыполненных зависимостей", intent.Id);
                    continue;
                }

                await ExecuteIntentAsync(intent, _cancellationTokenSource?.Token ?? cancellationToken);

                if (intent.Status == IntentStatus.Completed)
                {
                    completedCount++;
                }
                else if (intent.Status == IntentStatus.Failed)
                {
                    failedCount++;
                }
                else if (intent.Status == IntentStatus.Skipped)
                {
                    skippedCount++;
                }

                // Report progress
                progress?.Report(new()
                {
                    CompletedCount = completedCount,
                    TotalCount = sortedIntents.Count,
                    CurrentIntent = intent,
                    Message = $"Завершено {intent.Type} для {intent.Media?.Title}",
                });

                ProgressChanged?.Invoke(this, new()
                {
                    Intent = intent,
                    CompletedCount = completedCount,
                    TotalCount = sortedIntents.Count,
                    CurrentOperation = $"{intent.Type}: {intent.Media?.Title}",
                });
            }

            logger.LogInformation("Выполнение завершено. Обработано {CompletedCount}/{TotalCount} намерений",
                completedCount, sortedIntents.Count);

            // Log execution summary to audit system
            if (cancelledByUser)
            {
                logger.LogInformation("[АУДИТ] Выполнение синхронизации отменено: PlanId={PlanId}, Completed={Completed}, Failed={Failed}, Skipped={Skipped}, Total={Total}",
                    plan.Id, completedCount, failedCount, skippedCount, sortedIntents.Count);
            }
            else
            {
                logger.LogInformation("[АУДИТ] Выполнение синхронизации завершено: PlanId={PlanId}, Completed={Completed}, Failed={Failed}, Skipped={Skipped}, Total={Total}",
                    plan.Id, completedCount, failedCount, skippedCount, sortedIntents.Count);
            }

            // Trigger post-execution actualization to refresh Media state from all sources
            logger.LogInformation("Запуск послеоперационной актуализации для обновления состояния медиа");
            try
            {
                await orchestrator.GetStorageFullInfo();
                logger.LogInformation("Послеоперационная актуализация успешно завершена");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Послеоперационная актуализация не удалась: {ErrorMessage}", ex.Message);
                // Don't throw - actualization failure shouldn't fail the entire execution
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Критическая ошибка при выполнении синхронизации");
            logger.LogError("[АУДИТ] Выполнение синхронизации не удалось: PlanId={PlanId}, Error={Error}",
                plan.Id, ex.Message);

            throw;
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    public void Cancel()
    {
        logger.LogInformation("Запрошена отмена выполнения синхронизации");
        _cancellationTokenSource?.Cancel();
    }

    private List<IntentObject> ValidateSelectedIntents(SyncPlan plan)
    {
        logger.LogDebug("Проверка выбранных намерений из плана с {TotalCount} намерениями", plan.TotalCount);

        var selectedIntents = plan.Intents
            .Where(i => i.Status == IntentStatus.Selected)
            .ToList();

        if (selectedIntents.Count == 0)
        {
            logger.LogWarning("Намерения со статусом Selected не найдены");
            return selectedIntents;
        }

        logger.LogInformation("Найдено {SelectedCount} выбранных намерений из {TotalCount}",
            selectedIntents.Count, plan.TotalCount);

        return selectedIntents;
    }

    private List<IntentObject> TopologicalSort(List<IntentObject> intents)
    {
        logger.LogDebug("Выполнение топологической сортировки для {IntentCount} намерений", intents.Count);

        var sorted = new List<IntentObject>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        foreach (var intent in intents)
        {
            if (!visited.Contains(intent.Id))
            {
                TopologicalSortVisit(intent, intents, visited, visiting, sorted);
            }
        }

        logger.LogDebug("Топологическая сортировка завершена. Порядок: {IntentIds}",
            string.Join(" -> ", sorted.Select(i => $"{i.Type}:{i.Id[..8]}")));

        return sorted;
    }

    private void TopologicalSortVisit(
        IntentObject intent,
        List<IntentObject> allIntents,
        HashSet<string> visited,
        HashSet<string> visiting,
        List<IntentObject> sorted)
    {
        if (visiting.Contains(intent.Id))
        {
            logger.LogWarning("Circular dependency detected for intent {IntentId}", intent.Id);
            return;
        }

        if (visited.Contains(intent.Id))
        {
            return;
        }

        visiting.Add(intent.Id);

        // Visit dependencies first
        foreach (var dependency in intent.Dependencies)
        {
            // Only process dependencies that are in the selected intents list
            if (allIntents.Any(i => i.Id == dependency.Id))
            {
                TopologicalSortVisit(dependency, allIntents, visited, visiting, sorted);
            }
        }

        visiting.Remove(intent.Id);
        visited.Add(intent.Id);
        sorted.Add(intent);
    }

    private bool AreDependenciesSatisfied(IntentObject intent)
    {
        if (intent.Dependencies.Count == 0)
        {
            return true;
        }

        foreach (var dependency in intent.Dependencies)
        {
            if (dependency.Status != IntentStatus.Completed)
            {
                logger.LogDebug("Dependency {DependencyId} for intent {IntentId} is not completed (status: {Status})",
                    dependency.Id, intent.Id, dependency.Status);

                return false;
            }
        }

        return true;
    }

    private async Task ExecuteIntentAsync(IntentObject intent, CancellationToken cancellationToken)
    {
        logger.LogInformation("Выполнение намерения {IntentId}: {IntentType} для медиа {MediaId}",
            intent.Id, intent.Type, intent.MediaId);

        intent.Status = IntentStatus.Running;
        intent.LogOutput.Clear();

        // Log operation start to audit system
        LogAuditEvent(intent, "Начато", null);

        try
        {
            switch (intent.Type)
            {
                case IntentType.Download:
                    await ExecuteDownloadIntentAsync(intent, cancellationToken);
                    break;

                case IntentType.Upload:
                    await ExecuteUploadIntentAsync(intent, cancellationToken);
                    break;

                case IntentType.UpdateStatus:
                    await ExecuteUpdateStatusIntentAsync(intent, cancellationToken);
                    break;

                case IntentType.MarkAsDeleted:
                    await ExecuteMarkAsDeletedIntentAsync(intent, cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException($"Неизвестный тип намерения: {intent.Type}");
            }

            intent.Status = IntentStatus.Completed;
            intent.ExecutedAt = DateTime.UtcNow;
            logger.LogInformation("Намерение {IntentId} успешно завершено", intent.Id);

            // Log successful completion to audit system
            LogAuditEvent(intent, "Завершено", null);
        }
        catch (OperationCanceledException)
        {
            intent.Status = IntentStatus.Skipped;
            intent.ErrorMessage = "Операция была отменена";
            intent.ExecutedAt = DateTime.UtcNow;
            intent.LogOutput.AppendLine("Операция отменена пользователем");
            logger.LogWarning("Намерение {IntentId} было отменено", intent.Id);

            // Log cancellation to audit system
            LogAuditEvent(intent, "Отменено", "Операция отменена пользователем");
            throw; // Re-throw to stop execution
        }
        catch (UnauthorizedAccessException ex)
        {
            intent.Status = IntentStatus.Failed;
            intent.ErrorMessage = $"Ошибка аутентификации: {ex.Message}. Пожалуйста, обновите учетные данные.";
            intent.ExecutedAt = DateTime.UtcNow;
            intent.LogOutput.AppendLine($"Ошибка аутентификации: {ex.Message}");
            logger.LogWarning(ex, "Ошибка аутентификации для намерения {IntentId}", intent.Id);

            // Log failure to audit system
            LogAuditEvent(intent, "Ошибка", intent.ErrorMessage);
        }
        catch (HttpRequestException ex)
        {
            intent.Status = IntentStatus.Failed;
            intent.ErrorMessage = $"Сетевая ошибка: {ex.Message}. Проверьте соединение и повторите попытку.";
            intent.ExecutedAt = DateTime.UtcNow;
            intent.LogOutput.AppendLine($"Сетевая ошибка: {ex.Message}");
            logger.LogWarning(ex, "Сетевая ошибка для намерения {IntentId}", intent.Id);

            // Log failure to audit system
            LogAuditEvent(intent, "Ошибка", intent.ErrorMessage);
        }
        catch (IOException ex)
        {
            intent.Status = IntentStatus.Failed;
            intent.ErrorMessage = $"Ошибка хранилища: {ex.Message}. Проверьте свободное место и права доступа.";
            intent.ExecutedAt = DateTime.UtcNow;
            intent.LogOutput.AppendLine($"Ошибка хранилища: {ex.Message}");
            logger.LogError(ex, "Ошибка хранилища для намерения {IntentId}", intent.Id);

            // Log failure to audit system
            LogAuditEvent(intent, "Ошибка", intent.ErrorMessage);
        }
        catch (TimeoutException ex)
        {
            intent.Status = IntentStatus.Failed;
            intent.ErrorMessage = $"Тайм-аут операции: {ex.Message}. Повторите операцию.";
            intent.ExecutedAt = DateTime.UtcNow;
            intent.LogOutput.AppendLine($"Ошибка тайм-аута: {ex.Message}");
            logger.LogWarning(ex, "Тайм-аут для намерения {IntentId}", intent.Id);

            // Log failure to audit system
            LogAuditEvent(intent, "Ошибка", intent.ErrorMessage);
        }
        catch (Exception ex)
        {
            intent.Status = IntentStatus.Failed;
            intent.ErrorMessage = $"Непредвиденная ошибка: {ex.Message}";
            intent.ExecutedAt = DateTime.UtcNow;
            intent.LogOutput.AppendLine($"Ошибка: {ex.Message}");
            if (ex.StackTrace != null)
            {
                intent.LogOutput.AppendLine($"Стек вызовов: {ex.StackTrace}");
            }

            logger.LogError(ex, "Непредвиденная ошибка для намерения {IntentId}: {ErrorMessage}", intent.Id, ex.Message);

            // Log failure to audit system
            LogAuditEvent(intent, "Ошибка", intent.ErrorMessage);
        }
    }

    private void LogAuditEvent(IntentObject intent, string result, string? errorMessage)
    {
        var sourceName = intent.Source?.Title ?? "Н/А";
        var targetName = intent.Target?.Title ?? "Н/А";
        var mediaTitle = intent.Media?.Title ?? intent.MediaId;

        logger.LogInformation("[АУДИТ] Операция синхронизации: Type={OperationType}, Media={Media}, Source={Source}, Target={Target}, Result={Result}, Error={Error}",
            intent.Type,
            mediaTitle,
            sourceName,
            targetName,
            result,
            errorMessage ?? "Нет");
    }

    private async Task ExecuteDownloadIntentAsync(IntentObject intent, CancellationToken cancellationToken)
    {
        if (intent.Source == null)
        {
            throw new InvalidOperationException("Источник не задан для намерения скачивания");
        }

        if (string.IsNullOrEmpty(intent.ExternalId))
        {
            throw new InvalidOperationException("ExternalId не задан или пуст для намерения скачивания");
        }

        logger.LogInformation("Скачивание медиа {ExternalId} из источника {Source}",
            intent.ExternalId, intent.Source.Title);

        intent.LogOutput.AppendLine($"Начало скачивания из {intent.Source.Title}...");

        // Note: Current ISourceType interface doesn't support CancellationToken
        // This will be enhanced in future versions
        var downloadedMedia = await intent.Source.Type.Download(intent.ExternalId, intent.Source.Settings);

        intent.LogOutput.AppendLine($"Скачивание завершено: {downloadedMedia.Title}");
        logger.LogInformation("Медиа {MediaId} успешно скачано", intent.MediaId);
    }

    private async Task ExecuteUploadIntentAsync(IntentObject intent, CancellationToken cancellationToken)
    {
        if (intent.Target == null)
        {
            throw new InvalidOperationException("Цель не задана для намерения загрузки");
        }

        if (intent.Media == null)
        {
            throw new InvalidOperationException("Медиа не задано для намерения загрузки");
        }

        logger.LogInformation("Загрузка медиа {MediaId} в {Target}",
            intent.MediaId, intent.Target.Title);

        intent.LogOutput.AppendLine($"Начало загрузки в {intent.Target.Title}...");

        // Convert Media to MediaDto for plugin
        var mediaDto = new MediaDto
        {
            Id = intent.Media.Id,
            Title = intent.Media.Title,
            Description = intent.Media.Description,
            DataPath = string.Empty, // Will be populated by download intent
            PreviewPath = string.Empty,
            TempDataPath = string.Empty,
            TempPreviewPath = string.Empty,
        };

        // Note: Current ISourceType interface doesn't support CancellationToken
        var externalId = await intent.Target.Type.Upload(mediaDto, intent.Target.Settings);

        intent.LogOutput.AppendLine($"Загрузка завершена. Внешний ID: {externalId}");
        logger.LogInformation("Медиа {MediaId} успешно загружено. Внешний ID: {ExternalId}",
            intent.MediaId, externalId);

        // Store the external ID for database update
        intent.ExternalId = externalId;

        // Update MediaSourceLink in database
        UpdateMediaSourceLinkAfterUpload(intent, externalId);
    }

    private void UpdateMediaSourceLinkAfterUpload(IntentObject intent, string externalId)
    {
        if (intent.Media == null || intent.Target == null)
        {
            return;
        }

        logger.LogDebug("Обновление MediaSourceLink для медиа {MediaId} в {TargetId}",
            intent.MediaId, intent.Target.Id);

        // Check if link already exists
        var existingLink = intent.Media.Sources.FirstOrDefault(s => s.SourceId == intent.Target.Id);

        if (existingLink != null)
        {
            // Update existing link
            existingLink.ExternalId = externalId;
            existingLink.Status = MediaSourceLink.StatusOk;
            logger.LogDebug("Обновлена существующая связь MediaSourceLink для медиа {MediaId}", intent.MediaId);
        }
        else
        {
            // Create new link
            var newLink = new MediaSourceLink
            {
                MediaId = intent.MediaId,
                Media = intent.Media,
                SourceId = intent.Target.Id,
                ExternalId = externalId,
                Status = MediaSourceLink.StatusOk,
            };

            intent.Media.Sources.Add(newLink);
            logger.LogDebug("Создана новая связь MediaSourceLink для медиа {MediaId}", intent.MediaId);
        }

        // Persist to database
        orchestrator.UpdateMedia(intent.Media);
        intent.LogOutput.AppendLine($"База данных обновлена новой ссылкой на {intent.Target.Title}");
        logger.LogInformation("MediaSourceLink сохранена в базе данных для медиа {MediaId}", intent.MediaId);
    }

    private Task ExecuteUpdateStatusIntentAsync(IntentObject intent, CancellationToken cancellationToken)
    {
        if (intent.Source == null)
        {
            throw new InvalidOperationException("Источник не задан для намерения обновления статуса");
        }

        if (intent.Media == null)
        {
            throw new InvalidOperationException("Медиа не задано для намерения обновления статуса");
        }

        logger.LogInformation("Обновление статуса медиа {MediaId} в источнике {Source}",
            intent.MediaId, intent.Source.Title);

        intent.LogOutput.AppendLine($"Обновление статуса в {intent.Source.Title}...");

        // Find the MediaSourceLink for this source
        var link = intent.Media.Sources.FirstOrDefault(s => s.SourceId == intent.Source.Id);

        if (link != null)
        {
            // Update status based on whether media exists in source
            if (string.IsNullOrEmpty(intent.ExternalId))
            {
                // Media doesn't exist in source, mark as error or none
                link.Status = MediaSourceLink.StatusError;
                intent.LogOutput.AppendLine($"Медиа не найдено в {intent.Source.Title}, статус установлен в Error");
                logger.LogInformation("Медиа {MediaId} не найдено в источнике {Source}, статус обновлен на Error",
                    intent.MediaId, intent.Source.Title);
            }
            else
            {
                // Media exists, update to OK
                link.Status = MediaSourceLink.StatusOk;
                link.ExternalId = intent.ExternalId;
                intent.LogOutput.AppendLine($"Статус обновлен на OK в {intent.Source.Title}");
                logger.LogInformation("Статус медиа {MediaId} обновлен на OK в источнике {Source}",
                    intent.MediaId, intent.Source.Title);
            }

            // Persist to database
            orchestrator.UpdateMedia(intent.Media);
            intent.LogOutput.AppendLine("База данных успешно обновлена");
        }
        else
        {
            intent.LogOutput.AppendLine($"Существующая связь для {intent.Source.Title} не найдена");
            logger.LogWarning("Связь MediaSourceLink не найдена для медиа {MediaId} в источнике {Source}",
                intent.MediaId, intent.Source.Title);
        }

        return Task.CompletedTask;
    }

    private Task ExecuteMarkAsDeletedIntentAsync(IntentObject intent, CancellationToken cancellationToken)
    {
        if (intent.Source == null)
        {
            throw new InvalidOperationException("Источник не задан для намерения пометки как удаленного");
        }

        if (intent.Media == null)
        {
            throw new InvalidOperationException("Медиа не задано для намерения пометки как удаленного");
        }

        logger.LogInformation("Пометка медиа {MediaId} как удаленного в источнике {Source}",
            intent.MediaId, intent.Source.Title);

        intent.LogOutput.AppendLine($"Пометка как удаленного в {intent.Source.Title}...");

        // Find and remove the MediaSourceLink for this source
        var link = intent.Media.Sources.FirstOrDefault(s => s.SourceId == intent.Source.Id);

        if (link != null)
        {
            intent.Media.Sources.Remove(link);
            intent.LogOutput.AppendLine($"Удалена связь с {intent.Source.Title}");
            logger.LogInformation("Удалена связь MediaSourceLink для медиа {MediaId} из источника {Source}",
                intent.MediaId, intent.Source.Title);

            // Persist to database
            orchestrator.UpdateMedia(intent.Media);
            intent.LogOutput.AppendLine("База данных успешно обновлена");
        }
        else
        {
            intent.LogOutput.AppendLine($"Связь для {intent.Source.Title} не найдена (уже удалена)");
            logger.LogWarning("Связь MediaSourceLink не найдена для медиа {MediaId} в источнике {Source}",
                intent.MediaId, intent.Source.Title);
        }

        return Task.CompletedTask;
    }
}

public class IntentProgressEventArgs : EventArgs
{
    public IntentObject Intent { get; set; } = null!;
    public int CompletedCount { get; set; }
    public int TotalCount { get; set; }
    public string? CurrentOperation { get; set; }
}

public class ExecutionProgress
{
    public int CompletedCount { get; set; }
    public int TotalCount { get; set; }
    public IntentObject? CurrentIntent { get; set; }
    public string? Message { get; set; }
}
