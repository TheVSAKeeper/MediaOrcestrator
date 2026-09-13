using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.MediaContextMenu.Actions;

internal sealed class MetadataAction : IMediaMenuAction
{
    public int Order => 100;

    public IEnumerable<MenuItemSpec> Build(MediaSelection selection, MediaActionContext ctx)
    {
        if (!selection.IsBatch)
        {
            yield return new("Открыть детали")
            {
                Glyph = MenuIcons.Info,
                Execute = () =>
                {
                    ctx.Navigation.OpenInspector(selection.First);
                    return Task.CompletedTask;
                },
            };
        }

        var updateText = selection.IsBatch
            ? $"Принудительное обновление метаданных ({selection.Count})"
            : "Принудительное обновление метаданных";

        yield return new(updateText)
        {
            Glyph = MenuIcons.MetadataRefresh,
            Execute = () => UpdateAsync(selection.Items, ctx),
        };

        if (selection.SpecificSource != null)
        {
            var clearText = selection.IsBatch
                ? $"Очистить метаданные источника ({selection.SpecificSource.TitleFull}) ({selection.Count})"
                : $"Очистить метаданные источника ({selection.SpecificSource.TitleFull})";

            yield return new(clearText)
            {
                Glyph = MenuIcons.Delete,
                Execute = () => ClearAsync(selection.Items, selection.SpecificSource.Id, ctx),
            };
        }
        else
        {
            var clearText = selection.IsBatch
                ? $"Очистить все метаданные ({selection.Count})"
                : "Очистить все метаданные";

            yield return new(clearText)
            {
                Glyph = MenuIcons.Delete,
                Execute = () => ClearAsync(selection.Items, null, ctx),
            };
        }
    }

    private static Task UpdateAsync(IReadOnlyList<Media> mediaList, MediaActionContext ctx)
    {
        ctx.Logger.LogInformation("Запуск обновления метаданных для {Count} медиа", mediaList.Count);

        return BatchOperationRunner.RunAsync(mediaList,
            m => m.Title ?? string.Empty,
            async (m, ct) =>
            {
                await ctx.Orcestrator.ForceUpdateMetadataAsync(m, ct);
                ctx.Logger.LogInformation("Метаданные обновлены: '{Title}'", m.Title);
            },
            "Обновлено",
            "Обновление метаданных завершено с ошибками",
            ctx.Ui,
            ctx.Dialogs,
            ctx.Logger,
            ctx.ActionHolder,
            mediaList.Count > 1
                ? $"Обновление метаданных ({mediaList.Count})"
                : $"Обновление метаданных: «{mediaList[0].Title}»",
            ActionKind.Metadata);
    }

    private static Task ClearAsync(IReadOnlyList<Media> mediaList, string? sourceId, MediaActionContext ctx)
    {
        var actionName = sourceId is null
            ? mediaList.Count > 1
                ? $"Очистка всех метаданных ({mediaList.Count})"
                : $"Очистка всех метаданных: «{mediaList[0].Title}»"
            : mediaList.Count > 1
                ? $"Очистка метаданных источника ({mediaList.Count})"
                : $"Очистка метаданных источника: «{mediaList[0].Title}»";

        return BatchOperationRunner.RunAsync(mediaList,
            m => m.Title ?? string.Empty,
            (m, _) =>
            {
                if (sourceId != null)
                {
                    ctx.Logger.LogInformation("Очистка метаданных для медиа '{Title}', источник: {SourceId}", m.Title, sourceId);
                    ctx.Orcestrator.ClearSourceMetadata(m, sourceId);
                }
                else
                {
                    ctx.Logger.LogInformation("Очистка всех метаданных для медиа '{Title}'", m.Title);
                    ctx.Orcestrator.ClearAllMetadata(m);
                }

                return Task.CompletedTask;
            },
            "Очищено",
            "Очистка метаданных завершена с ошибками",
            ctx.Ui,
            ctx.Dialogs,
            ctx.Logger,
            ctx.ActionHolder,
            actionName,
            ActionKind.Metadata);
    }
}
