using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class TransfersViewModel
{
    internal void OpenInMedia(Media media)
    {
        try
        {
            _navigation.OpenInspector(media);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось открыть медиа {Media} в разделе Медиа", media.Title);
        }
    }

    internal void OpenSourcesGraph(SourceSyncRelation relation)
    {
        try
        {
            _navigation.OpenSourcesGraph(relation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Не удалось открыть связь {From} → {To} в разделе Источники и связи",
                relation.From.TitleFull,
                relation.To.TitleFull);
        }
    }

    private bool CanRunSingleStep(TransferIntentNodeViewModel? node) => node is not null && CanRunPlan;

    [RelayCommand(CanExecute = nameof(CanRunSingleStep))]
    private async Task RunSingleStepAsync(TransferIntentNodeViewModel? node)
    {
        RefreshPlanState();
        if (node is null || !CanRunPlan)
        {
            return;
        }

        IsRunning = true;
        AuthenticationRecovery.Failures.Clear();
        StatusText = $"Точечный прогон: {node.Intent.Media.Title}";
        node.ResetRunState();

        using var cts = new CancellationTokenSource();
        _runCts = cts;

        RunStepsTotal = 1;
        RunStepsCompleted = 0;
        RunStepsFailed = 0;

        var action = _actionHolder.Register(
            $"Передача (одна): {node.Intent.Media.Title}",
            "Старт…",
            1,
            cts,
            kind: ActionKind.Sync);

        AppendJournal(TransferJournalLevel.Info, $"▶ Точечный прогон: {node.Intent.Media.Title} · {node.Route}");

        var failed = false;
        var cancelled = false;
        var skipped = false;
        Exception? error = null;

        try
        {
            using (_actionHolder.BeginScope(action))
            {
                var progress = new DirectProgress<SyncAttemptStatus>(s => HandleAttemptStatus(node.Intent, node, s));
                var outcome = await _retryRunner.RunAsync(node.Intent.Media, node.Intent.Relation, progress, cancellationToken: cts.Token);
                skipped = outcome == SyncRunOutcome.Skipped;
                action.ProgressPlus();
                RunStepsCompleted = 1;
            }

            StatusText = skipped ? "Пропущено" : "Завершено";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            cancelled = true;
            StatusText = "Остановлено";
            AppendJournal(TransferJournalLevel.Warning, "Точечный прогон остановлен.");
        }
        catch (Exception ex)
        {
            failed = true;
            AuthenticationRecovery.Report(ex);
            error = ex;
            RunStepsCompleted = 1;
            RunStepsFailed = 1;
            _logger.LogError(ex, "Ошибка точечного прогона {Intent}", node.Intent);
            StatusText = $"Ошибка: {ex.Message}";
            AppendJournal(TransferJournalLevel.Error, $"Ошибка точечного прогона: {ex.Message}");
        }
        finally
        {
            if (cancelled || cts.IsCancellationRequested)
            {
                action.MarkCancelled("Прервано");
            }
            else if (failed)
            {
                action.Fail("Ошибка: " + error?.Message, error);
            }
            else
            {
                action.Finish(skipped ? "Пропущено" : "Готово");
            }

            IsRunning = false;
            RefreshPlanState();
            _runCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private async Task ExecuteSelectedAsync(CancellationToken externalToken)
    {
        RefreshPlanState();
        if (!CanExecute())
        {
            return;
        }

        ResyncIntentSelectionFromTree();

        var selectedRoots = CollectSelectedRoots();

        if (selectedRoots.Count == 0)
        {
            return;
        }

        IsRunning = true;
        AuthenticationRecovery.Failures.Clear();
        StatusText = "Старт…";
        ResetRunStatesForRoots(selectedRoots);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _runCts = cts;

        var totalUnits = SelectedSteps > 0 ? SelectedSteps : selectedRoots.Count;
        RunStepsTotal = totalUnits;
        RunStepsCompleted = 0;
        RunStepsFailed = 0;
        var action = _actionHolder.Register(
            $"Передачи: {selectedRoots.Count} цепочек",
            "Старт…",
            totalUnits,
            cts,
            kind: ActionKind.Sync);

        AppendJournal(TransferJournalLevel.Info, $"[Старт] {selectedRoots.Count} цепочек · {totalUnits} шагов");

        var syncErrors = new List<Exception>();
        var cancelled = false;
        var stoppedByError = false;
        var skippedSteps = 0;
        var relations = _orcestrator.GetRelations();
        var executor = new ParallelSyncExecutor(selectedRoots, relations);
        var nodeMap = BuildNodeMap();

        try
        {
            using (_actionHolder.BeginScope(action))
            {
                await executor.ExecuteAsync(
                    async intent =>
                    {
                        var node = nodeMap.GetValueOrDefault(intent);

                        StatusText = $"Обработка: {intent.Media.Title}";
                        action.Status = $"Обработка: {intent.Media.Title}";

                        var progress = new DirectProgress<SyncAttemptStatus>(status => HandleAttemptStatus(intent, node, status));

                        var outcome = await _retryRunner.RunAsync(intent.Media, intent.Relation, progress, cancellationToken: cts.Token);
                        action.ProgressPlus();
                        InvokeOnUi(() => RunStepsCompleted++);

                        if (outcome == SyncRunOutcome.Skipped)
                        {
                            Interlocked.Increment(ref skippedSteps);
                            return outcome;
                        }

                        if (UploadIntervalEnabled && UploadIntervalMinutes is { } pauseMinutes)
                        {
                            StatusText = $"Пауза {pauseMinutes} мин после загрузки";
                            action.Status = $"Пауза {pauseMinutes} мин после загрузки";
                            AppendJournal(TransferJournalLevel.Info, $"«{intent.Media.Title}» загружено, пауза {pauseMinutes} мин");

                            await Task.Delay(TimeSpan.FromMinutes(pauseMinutes), cts.Token);
                        }

                        return outcome;
                    },
                    cts.Token,
                    (intent, ex) =>
                    {
                        var node = nodeMap.GetValueOrDefault(intent);
                        InvokeOnUi(() =>
                        {
                            node?.ApplyStatus(new(SyncAttemptKind.FailedFinal, 0, 0, Error: ex));
                            AppendJournal(TransferJournalLevel.Error, $"Ошибка: {intent.Media.Title} – {ex.Message}");
                            RunStepsCompleted++;
                            RunStepsFailed++;
                            AuthenticationRecovery.Report(ex);
                            syncErrors.Add(ex);
                        });

                        _logger.LogError(ex, "Ошибка передачи {Intent}", intent);

                        if (!StopOnError)
                        {
                            return;
                        }

                        stoppedByError = true;
                        AppendJournal(TransferJournalLevel.Warning, "Прервано из-за ошибки (включён режим «Прервать при ошибке»).");
                        cts.Cancel();
                    });
            }

            StatusText = skippedSteps > 0 ? $"Завершено (пропущено: {skippedSteps})" : "Завершено";
            AppendJournal(TransferJournalLevel.Success, "Прогон завершён.");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            cancelled = true;
            StatusText = stoppedByError ? "Остановлено после ошибки" : "Остановлено";
            AppendJournal(TransferJournalLevel.Warning, stoppedByError ? "Остановлено после ошибки." : "Прогон остановлен пользователем.");
        }
        catch (Exception ex)
        {
            syncErrors.Add(ex);
            _logger.LogError(ex, "Критическая ошибка прогона передач");
            StatusText = $"Критическая ошибка: {ex.Message}";
            AppendJournal(TransferJournalLevel.Error, $"Критическая ошибка: {ex.Message}");
        }
        finally
        {
            if (cancelled || cts.IsCancellationRequested)
            {
                action.MarkCancelled(syncErrors.Count > 0 ? $"Прервано: ошибок {syncErrors.Count}" : "Прервано");
            }
            else if (syncErrors.Count > 0)
            {
                action.Fail($"Ошибок: {syncErrors.Count}", new AggregateException(syncErrors));
            }
            else
            {
                action.Finish(skippedSteps > 0 ? $"Готово (пропущено: {skippedSteps})" : "Готово");
            }

            IsRunning = false;
            RefreshPlanState();
            _runCts = null;
        }
    }

    private bool CanExecute() => CanRunPlan && HasAnySelected && HasPlan && IsUploadIntervalValid;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        if (_runCts is null)
        {
            return;
        }

        AppendJournal(TransferJournalLevel.Warning, "Запрошена остановка прогона…");
        _runCts.Cancel();
    }

    private bool CanStop() => IsRunning;

    private List<SyncIntent> CollectSelectedRoots()
    {
        var roots = new List<SyncIntent>();

        foreach (var group in Groups)
        {
            foreach (var child in group.Children)
            {
                if (child.IsSelected)
                {
                    roots.Add(child.Intent);
                }
            }
        }

        return roots;
    }

    private void ResyncIntentSelectionFromTree()
    {
        ClearIntentSelectionRecursive(_allRootIntents);

        foreach (var group in Groups)
        {
            foreach (var node in group.Children)
            {
                ApplyNodeSelectionToIntent(node);
            }
        }
    }

    private static void ClearIntentSelectionRecursive(IEnumerable<SyncIntent> intents)
    {
        foreach (var intent in intents)
        {
            intent.IsSelected = false;
            ClearIntentSelectionRecursive(intent.NextIntents);
        }
    }

    private static void ApplyNodeSelectionToIntent(TransferIntentNodeViewModel node)
    {
        node.Intent.IsSelected = node.IsSelected;

        foreach (var child in node.Children)
        {
            ApplyNodeSelectionToIntent(child);
        }
    }

    private Dictionary<SyncIntent, TransferIntentNodeViewModel> BuildNodeMap()
    {
        var map = new Dictionary<SyncIntent, TransferIntentNodeViewModel>();

        foreach (var group in Groups)
        {
            foreach (var child in group.Children)
            {
                AddNodeRecursive(child, map);
            }
        }

        return map;
    }

    private static void AddNodeRecursive(TransferIntentNodeViewModel node, Dictionary<SyncIntent, TransferIntentNodeViewModel> map)
    {
        map[node.Intent] = node;

        foreach (var child in node.Children)
        {
            AddNodeRecursive(child, map);
        }
    }

    private void ResetRunStatesForRoots(List<SyncIntent> roots)
    {
        var nodeMap = BuildNodeMap();

        foreach (var root in roots)
        {
            if (nodeMap.TryGetValue(root, out var node))
            {
                ResetRecursive(node);
            }
        }
    }

    private static void ResetRecursive(TransferIntentNodeViewModel node)
    {
        node.ResetRunState();

        foreach (var child in node.Children)
        {
            ResetRecursive(child);
        }
    }

    private void HandleAttemptStatus(SyncIntent intent, TransferIntentNodeViewModel? node, SyncAttemptStatus status)
    {
        InvokeOnUi(() =>
        {
            node?.ApplyStatus(status);

            switch (status.Kind)
            {
                case SyncAttemptKind.Started when status.AttemptNumber == 1:
                    AppendJournal(TransferJournalLevel.Info, $"▶ {intent.Media.Title} · {intent.From.TypeId} → {intent.To.TypeId}");
                    break;

                case SyncAttemptKind.Succeeded:
                    AppendJournal(TransferJournalLevel.Success, $"✓ {intent.Media.Title} → {intent.To.Title}");

                    if (node is not null)
                    {
                        node.SetIsSelectedSilent(false);
                        FindGroupOf(node)?.Recompute();
                        RecomputeCounts();
                    }

                    break;

                case SyncAttemptKind.FailedRetrying:
                {
                    var minutes = status.NextDelay?.TotalMinutes ?? 0;
                    AppendJournal(
                        TransferJournalLevel.Warning,
                        $"⚠ {intent.Media.Title}: {status.Error?.Message}. Попытка {status.AttemptNumber + 1}/{status.MaxAttempts} через {minutes:F0} мин");
                    break;
                }

                case SyncAttemptKind.NonRetriable:
                    AppendJournal(TransferJournalLevel.Error, $"⛔ {intent.Media.Title}: {status.Error?.Message} (не ретраим)");
                    break;

                case SyncAttemptKind.Joined:
                    AppendJournal(TransferJournalLevel.Info, $"⏳ {intent.Media.Title}: операция уже выполняется, ждём");
                    break;

                case SyncAttemptKind.Skipped:
                    AppendJournal(TransferJournalLevel.Info, $"[Пропущено] {intent.Media.Title}: {status.Error?.Message}");

                    if (node is not null)
                    {
                        ClearDescendantsSilent(node);
                        FindGroupOf(node)?.Recompute();
                        RecomputeCounts();
                    }

                    break;
            }
        });
    }

    private void AppendJournal(TransferJournalLevel level, string text)
    {
        InvokeOnUi(() =>
        {
            Journal.Insert(0, new(DateTime.Now, level, text));

            while (Journal.Count > JournalMaxEntries)
            {
                Journal.RemoveAt(Journal.Count - 1);
            }
        });
    }

    private void InvokeOnUi(Action action) => _dispatcher.Invoke(action);

    private sealed class DirectProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }
}
