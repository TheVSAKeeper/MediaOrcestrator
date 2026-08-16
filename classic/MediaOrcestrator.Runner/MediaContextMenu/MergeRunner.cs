using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.MediaContextMenu;

internal static class MergeRunner
{
    public static void Run(IReadOnlyList<Media> ordered, MediaActionContext ctx)
    {
        if (ordered.Count < 2)
        {
            return;
        }

        ctx.Logger.LogInformation("Запуск операции объединения медиа. Выбрано элементов: {Count}", ordered.Count);

        try
        {
            var preview = ctx.MergeService.BuildPreview(ordered);
            var allSources = ctx.Orcestrator.GetSources();

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

            var result = MessageBox.Show(ctx.Ui.Owner,
                confirmation,
                $"Объединение {ordered.Count} медиа",
                MessageBoxButtons.YesNo,
                preview.HasConflicts ? MessageBoxIcon.Warning : MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                ctx.Logger.LogInformation("Объединение медиа отменено пользователем.");
                return;
            }

            ctx.MergeService.Apply(preview, true);
            ctx.Ui.NotifyDataChanged();
        }
        catch (Exception ex)
        {
            ctx.Logger.LogError(ex, "Ошибка при объединении медиа.");

            MessageBox.Show(ctx.Ui.Owner,
                $"""
                 Произошла ошибка при объединении медиа:

                 {ex.Message}
                 """,
                "Ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
