using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.MediaContextMenu.Actions;

internal sealed class RemoveFromDatabaseAction : IMediaMenuAction
{
    public int Order => 710;

    public IEnumerable<MenuItemSpec> Build(MediaSelection selection, MediaActionContext ctx)
    {
        if (selection.Items.Count == 0)
        {
            yield break;
        }

        var text = selection.IsBatch
            ? $"Удалить из базы оркестратора ({selection.Items.Count})"
            : "Удалить из базы оркестратора";

        yield return new(text)
        {
            Glyph = MenuIcons.Delete,
            Execute = () => ExecuteAsync(selection.Items, ctx),
        };
    }

    private static Task ExecuteAsync(IReadOnlyList<Media> mediaList, MediaActionContext ctx)
    {
        var subject = mediaList.Count == 1
            ? $"«{mediaList[0].Title}»"
            : $"{mediaList.Count} медиа";

        var message = $"""
                       Убрать {subject} из базы оркестратора?

                       На площадках ничего не удалится. Если медиа снова найдётся при сканировании, оно вернётся в список.
                       """;

        if (!ctx.Dialogs.ConfirmWarning("Удаление из базы", message))
        {
            ctx.Logger.LogInformation("Пользователь отменил удаление из базы");
            return Task.CompletedTask;
        }

        return BatchOperationRunner.RunAsync(mediaList,
            m => m.Title ?? string.Empty,
            (m, _) =>
            {
                ctx.Orcestrator.RemoveMedia(m);
                ctx.Logger.LogInformation("Медиа '{Title}' удалено из базы оркестратора", m.Title);
                return Task.CompletedTask;
            },
            "Удалено из базы",
            "Удаление из базы завершено с ошибками",
            ctx.Ui,
            ctx.Dialogs,
            ctx.Logger,
            ctx.ActionHolder,
            $"Удаление из базы ({mediaList.Count})",
            ActionKind.Delete);
    }
}
