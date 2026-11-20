using KeepShell.Diagnostics;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using MediaOrcestrator.Runner.Wpf.Views.Comments;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

internal static class StartupPreloader
{
    public static void StartInBackground(IServiceProvider services, Dispatcher dispatcher)
    {
        var actionHolder = services.GetRequiredService<ActionHolder>();
        var sources = services.GetRequiredService<SourcesRelationsViewModel>();
        var media = services.GetRequiredService<MediaViewModel>();
        var audit = services.GetRequiredService<AuditViewModel>();
        var comments = services.GetRequiredService<CommentsViewModel>();
        var transfers = services.GetRequiredService<TransfersViewModel>();
        var tools = services.GetRequiredService<ToolsViewModel>();
        var updateManager = services.GetRequiredService<AppUpdateManager>();
        var appUpdate = services.GetRequiredService<AppUpdateViewModel>();
        var updatePreferences = services.GetRequiredService<UpdatePreferences>();
        var preloadSignal = services.GetRequiredService<PreloadCompletionSignal>();
        var performanceMonitor = services.GetRequiredService<PerformanceMonitor>();

        string? appUpdateNote = null;

        var dbSteps = new PreloadStep[]
        {
            new("Источники и связи", _ => sources.EnsureLoadedAsync()),
            new("Медиа", _ => media.EnsureLoadedAsync()),
            new("Аудит источников", _ => audit.EnsureLoadedAsync()),
            new("Комментарии", _ => comments.EnsureLoadedAsync()),
            new("План передач", _ => transfers.EnsureLoadedAsync()),
        };

        var ioSteps = BuildIoSteps(
            tools,
            updateManager,
            appUpdate,
            updatePreferences,
            dispatcher,
            note => appUpdateNote = note);

        var totalSteps = dbSteps.Length + ioSteps.Length;

        _ = Task.Run(async () =>
        {
            var cts = new CancellationTokenSource();
            var action = actionHolder.Register(
                "Предзагрузка данных",
                "Подготовка…",
                progressMax: totalSteps,
                ctx: cts,
                kind: ActionKind.Other);

            var failed = 0;

            try
            {
                performanceMonitor.SetPhase("Предзагрузка");
                using var scope = actionHolder.BeginScope(action);

                var ioTasks = ioSteps
                    .Select(step => Task.Run(async () =>
                    {
                        if (!await RunStepAsync(actionHolder, action, step, cts))
                        {
                            Interlocked.Increment(ref failed);
                        }
                    }, cts.Token))
                    .ToArray();

                foreach (var step in dbSteps)
                {
                    if (!await RunStepAsync(actionHolder, action, step, cts))
                    {
                        failed++;
                    }
                }

                await Task.WhenAll(ioTasks);

                var finalStatus = appUpdateNote
                    ?? (failed > 0 ? $"Готово, шагов с ошибками: {failed}" : "Готово");

                action.Finish(finalStatus);
                Log.Information("Фоновая предзагрузка данных завершена (ошибок шагов: {Failed})", failed);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                action.MarkCancelled("Отменено");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Фоновая предзагрузка данных прервана");
                action.Fail("Ошибка: " + ex.Message, ex);
            }
            finally
            {
                performanceMonitor.SetPhase(null);
                preloadSignal.Complete();
                cts.Dispose();
            }
        });
    }

    private static async Task<bool> RunStepAsync(
        ActionHolder actionHolder,
        ActionHolder.RunningAction parent,
        PreloadStep step,
        CancellationTokenSource parentCancellation)
    {
        var stepCts = CancellationTokenSource.CreateLinkedTokenSource(parentCancellation.Token);
        var child = actionHolder.Register(
            step.Status,
            "Выполняется…",
            progressMax: 1,
            ctx: stepCts,
            kind: ActionKind.Other,
            parent: parent);

        try
        {
            using var scope = actionHolder.BeginScope(child);
            await step.Action(stepCts.Token);
            child.SetProgress(1);
            child.Finish("Готово");
            return true;
        }
        catch (OperationCanceledException) when (stepCts.IsCancellationRequested)
        {
            child.MarkCancelled("Отменено");
            await parentCancellation.CancelAsync();
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Шаг предзагрузки «{Step}» не удался", step.Status);
            child.Fail("Ошибка: " + ex.Message, ex);
            return false;
        }
        finally
        {
            actionHolder.ProgressPlus(parent.Id);
        }
    }

    internal sealed record PreloadStep(string Status, Func<CancellationToken, Task> Action);

    internal static PreloadStep[] BuildIoSteps(
        ToolsViewModel tools,
        AppUpdateManager updateManager,
        AppUpdateViewModel appUpdate,
        UpdatePreferences updatePreferences,
        Dispatcher dispatcher,
        Action<string> onUpdateNote)
    {
        List<PreloadStep> steps =
        [
            new("Проверка инструментов", _ => SafeCheckToolsAsync(tools)),
            new("Прогрев редактора ответов", _ => SafePreheatReplyEditorAsync(dispatcher)),
        ];

        if (updatePreferences.CheckOnStartup)
        {
            steps.Insert(1, new("Проверка обновлений приложения", async ct =>
            {
                var note = await SafeCheckAppUpdateAsync(updateManager, appUpdate, ct);

                if (!string.IsNullOrWhiteSpace(note))
                {
                    onUpdateNote(note);
                }
            }));
        }

        return [.. steps];
    }

    private static async Task SafePreheatReplyEditorAsync(Dispatcher dispatcher)
    {
        try
        {
            await dispatcher.InvokeAsync(
                () => new ReplyEditorView(),
                DispatcherPriority.Background);
        }
        catch (OperationCanceledException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Прогрев ReplyEditorView не удался");
        }
    }

    private static async Task SafeCheckToolsAsync(ToolsViewModel tools)
    {
        try
        {
            await tools.EnsureCheckedAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Фоновая проверка инструментов не удалась");
        }
    }

    private static async Task<string?> SafeCheckAppUpdateAsync(AppUpdateManager updateManager, AppUpdateViewModel viewModel, CancellationToken token)
    {
        try
        {
            var update = await updateManager.CheckForUpdateAsync(token);
            viewModel.SetPendingUpdate(update);

            if (update is null)
            {
                return null;
            }

            Log.Information("Доступно обновление приложения: {Version}", update.Version);
            return $"Доступно обновление приложения: {update.Version}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Фоновая проверка обновлений приложения не удалась");
            return null;
        }
    }
}
