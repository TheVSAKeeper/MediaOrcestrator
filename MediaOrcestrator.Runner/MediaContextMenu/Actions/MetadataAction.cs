using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.MediaContextMenu.Actions;

internal sealed class MetadataAction : IMediaMenuAction
{
    public int Order => 100;

    public IEnumerable<MenuItemSpec> Build(MediaSelection selection, MediaActionContext ctx)
    {
        if (!selection.IsBatch)
        {
            yield return new("Просмотр метаданных медиа", MenuIcons.Info)
            {
                Execute = () => ShowDetailAsync(selection.First, ctx),
            };
        }

        var updateText = selection.IsBatch
            ? $"Принудительное обновление метаданных ({selection.Count})"
            : "Принудительное обновление метаданных";

        yield return new(updateText, MenuIcons.Sync)
        {
            Execute = () => UpdateAsync(selection.Items, ctx),
        };

        if (selection.IsBatch)
        {
            yield break;
        }

        if (selection.SpecificSource != null)
        {
            yield return new($"Очистить метаданные источника ({selection.SpecificSource.TitleFull})", MenuIcons.Delete)
            {
                Execute = () => ClearAsync(selection.First, selection.SpecificSource.Id, ctx),
            };
        }
        else
        {
            yield return new("Очистить все метаданные", MenuIcons.Delete)
            {
                Execute = () => ClearAsync(selection.First, null, ctx),
            };
        }
    }

    private static Task ShowDetailAsync(Media media, MediaActionContext ctx)
    {
        var logger = ctx.LoggerFactory.CreateLogger<MediaDetailForm>();
        var form = new MediaDetailForm(media, ctx.Orcestrator, ctx.ActionHolder, ctx.CommentsService, logger);
        form.Show(ctx.Ui.Owner);
        return Task.CompletedTask;
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
            ctx.Logger,
            ctx.ActionHolder,
            mediaList.Count > 1
                ? $"Обновление метаданных ({mediaList.Count})"
                : $"Обновление метаданных: «{mediaList[0].Title}»");
    }

    private static Task ClearAsync(Media media, string? sourceId, MediaActionContext ctx)
    {
        if (sourceId != null)
        {
            ctx.Logger.LogInformation("Очистка метаданных для медиа '{Title}', источник: {SourceId}", media.Title, sourceId);
            ctx.Orcestrator.ClearSourceMetadata(media, sourceId);
        }
        else
        {
            ctx.Logger.LogInformation("Очистка всех метаданных для медиа '{Title}'", media.Title);
            ctx.Orcestrator.ClearAllMetadata(media);
        }

        ctx.Ui.NotifyDataChanged();
        return Task.CompletedTask;
    }
}
