using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Merging;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.MediaContextMenu;

internal static class MergeRunner
{
    public static async Task RunAsync(IReadOnlyList<Media> ordered, MediaActionContext ctx)
    {
        if (ordered.Count < 2)
        {
            return;
        }

        ctx.Logger.LogInformation("Запуск операции объединения медиа. Выбрано элементов: {Count}", ordered.Count);

        MergePreview preview;
        var allSources = ctx.Orcestrator.GetSources();

        try
        {
            preview = await Task.Run(() => ctx.MergeService.BuildPreview(ordered));
        }
        catch (Exception ex)
        {
            ctx.Logger.LogError(ex, "Не удалось построить превью объединения медиа.");
            ctx.Dialogs.Error("Объединение",
                $"Не удалось построить превью объединения: {ex.Message}");
            return;
        }

        var mediaList = string.Join("\n", preview.SourceMedias.Select(m => FormatMedia(m, allSources)));
        var conflictsNote = preview.HasConflicts
            ? "\n⚠ Дублирующиеся источники будут пропущены."
            : string.Empty;

        var confirmation = $"""
                            Целевое (сохранится):
                            {FormatMedia(preview.TargetMedia, allSources)}

                            Будут присоединены и удалены:
                            {mediaList}
                            {conflictsNote}
                            """;

        var confirmed = preview.HasConflicts
            ? ctx.Dialogs.ConfirmWarning($"Объединение {ordered.Count} медиа", confirmation)
            : ctx.Dialogs.Confirm($"Объединение {ordered.Count} медиа", confirmation);

        if (!confirmed)
        {
            ctx.Logger.LogInformation("Объединение медиа отменено пользователем.");
            return;
        }

        using var cts = new CancellationTokenSource();
        var actionName = $"Объединение {ordered.Count} медиа: «{preview.TargetMedia.Title}»";
        var action = ctx.ActionHolder.Register(actionName, "Применение…", 0, cts, kind: ActionKind.Metadata);

        ctx.Ui.SetLoading(true);

        try
        {
            await Task.Run(() => ctx.MergeService.Apply(preview, true), cts.Token);
            action.Finish($"Объединено: {ordered.Count}");
        }
        catch (OperationCanceledException)
        {
            action.MarkCancelled("Отменено");
        }
        catch (Exception ex)
        {
            ctx.Logger.LogError(ex, "Ошибка при объединении медиа.");
            action.Fail("Ошибка: " + ex.Message, ex);
        }
        finally
        {
            ctx.Ui.SetLoading(false);
            ctx.Ui.NotifyDataChanged();
        }
    }

    private static string FormatMedia(Media media, IReadOnlyList<Source> allSources)
    {
        var sourceNames = media.Sources
            .Select(s => allSources.FirstOrDefault(x => x.Id == s.SourceId)?.Title ?? s.SourceId)
            .ToList();

        return $"- {media.Title} [{string.Join(", ", sourceNames)}]";
    }
}
