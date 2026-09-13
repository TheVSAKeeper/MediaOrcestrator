using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.MediaContextMenu;

internal static class BatchOperationRunner
{
    public static async Task RunAsync<T>(
        IReadOnlyList<T> items,
        Func<T, string> titleSelector,
        Func<T, CancellationToken, Task> operation,
        string bodyPrefix,
        string errorTitle,
        IMediaActionUi ui,
        IDialogService dialogs,
        ILogger logger,
        ActionHolder? actionHolder = null,
        string? actionName = null,
        ActionKind kind = ActionKind.Other,
        CancellationToken externalCt = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var token = linkedCts.Token;

        ActionHolder.RunningAction? running = null;

        if (actionHolder != null && !string.IsNullOrEmpty(actionName))
        {
            running = actionHolder.Register(actionName, "В процессе", items.Count, linkedCts, kind: kind);
        }

        var etaTicker = running != null ? new SubtitleEtaTicker(running, new()) : null;

        using var batchScope = running != null && actionHolder != null
            ? actionHolder.BeginScope(running)
            : null;

        ui.SetLoading(true);
        var errors = new List<(T item, Exception ex)>();
        var processed = 0;

        try
        {
            foreach (var item in items)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await operation(item, token);
                    Advance();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка операции для '{Title}'", titleSelector(item));
                    errors.Add((item, ex));
                    Advance();
                }
            }

            if (errors.Count > 0)
            {
                ShowErrors(bodyPrefix, errorTitle, processed - errors.Count, items.Count, errors, titleSelector, dialogs);
            }
        }
        finally
        {
            if (running != null)
            {
                if (token.IsCancellationRequested)
                {
                    running.MarkCancelled();
                }
                else if (errors.Count > 0)
                {
                    var message = $"Ошибок: {errors.Count} из {items.Count}";
                    var details = string.Join(Environment.NewLine,
                        errors.Select(e => $"- {titleSelector(e.item)}: {e.ex.Message}"));

                    running.Fail(message, new AggregateException(message + Environment.NewLine + details,
                        errors.Select(e => e.ex)));
                }
                else
                {
                    running.Finish();
                }
            }

            ui.SetLoading(false);
            ui.NotifyDataChanged();
        }

        return;

        void Advance()
        {
            running?.ProgressPlus();
            processed++;

            if (running == null)
            {
                return;
            }

            running.Status = $"{processed} / {items.Count}";
            etaTicker?.Report(processed * 100.0 / Math.Max(items.Count, 1));
        }
    }

    private static void ShowErrors<T>(
        string bodyPrefix,
        string title,
        int succeeded,
        int total,
        List<(T item, Exception ex)> errors,
        Func<T, string> titleSelector,
        IDialogService dialogs)
    {
        if (errors.Count == 0)
        {
            return;
        }

        var details = string.Join("\n", errors.Select(e => $"- {titleSelector(e.item)}: {e.ex.Message}"));

        dialogs.Warning(title,
            $"""
             {bodyPrefix}: {succeeded} из {total}

             Ошибки:
             {details}
             """);
    }
}
