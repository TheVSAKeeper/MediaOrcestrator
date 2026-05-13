using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Runner.MediaContextMenu.Actions;

internal sealed class SkipPlanAction : IMediaMenuAction
{
    public int Order => 400;

    public IEnumerable<MenuItemSpec> Build(MediaSelection selection, MediaActionContext ctx)
    {
        var allSources = ctx.Orcestrator.GetSources().Where(s => !s.IsDisable).ToList();

        var toSkip = new List<(Media media, string sourceId)>();
        foreach (var media in selection.Items)
        {
            foreach (var source in allSources)
            {
                var link = media.Sources.FirstOrDefault(s => s.SourceId == source.Id);

                if (link is not { Status: MediaStatus.Ok or MediaStatus.PartialOk or MediaStatus.Skipped })
                {
                    toSkip.Add((media, source.Id));
                }
            }
        }

        if (toSkip.Count > 0)
        {
            yield return new($"Пропустить выбранное везде, где не синхронизировано ({toSkip.Count})", MenuIcons.Skip)
            {
                Execute = () => Run(toSkip, true, ctx),
            };
        }

        var skippedLinks = selection.Items
            .SelectMany(m => m.Sources
                .Where(s => s.Status == MediaStatus.Skipped)
                .Select(s => (media: m, sourceId: s.SourceId)))
            .ToList();

        if (skippedLinks.Count > 0)
        {
            yield return new($"Снять весь пропуск с выбранных ({skippedLinks.Count})", MenuIcons.Unskip)
            {
                Execute = () => Run(skippedLinks, false, ctx),
            };
        }
    }

    private static Task Run(IReadOnlyList<(Media media, string sourceId)> pairs, bool skip, MediaActionContext ctx)
    {
        var bodyPrefix = skip ? "Помечено пропуском" : "Снят пропуск";
        var errorTitle = skip ? "Пакетный пропуск завершён с ошибками" : "Пакетное снятие пропуска завершено с ошибками";

        BatchOperationRunner.Run(pairs,
            p => p.media.Title ?? string.Empty,
            p =>
            {
                if (skip)
                {
                    ctx.Orcestrator.SetSourceSkipped(p.media, p.sourceId);
                }
                else
                {
                    ctx.Orcestrator.UnsetSourceSkipped(p.media, p.sourceId);
                }
            },
            bodyPrefix,
            errorTitle,
            ctx.Ui,
            ctx.Logger);

        return Task.CompletedTask;
    }
}
