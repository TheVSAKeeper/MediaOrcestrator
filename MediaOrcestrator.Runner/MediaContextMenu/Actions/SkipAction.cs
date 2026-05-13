using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Runner.MediaContextMenu.Actions;

internal sealed class SkipAction : IMediaMenuAction
{
    public int Order => 300;

    public IEnumerable<MenuItemSpec> Build(MediaSelection selection, MediaActionContext ctx)
    {
        var sources = selection.SpecificSource != null
            ? [selection.SpecificSource]
            : ctx.Orcestrator.GetSources().Where(s => !s.IsDisable).ToList();

        foreach (var source in sources)
        {
            if (source.IsDisable)
            {
                continue;
            }

            foreach (var item in BuildForSource(selection, source, ctx))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<MenuItemSpec> BuildForSource(MediaSelection selection, Source source, MediaActionContext ctx)
    {
        var toSkip = selection.Items.Where(m => CanSkip(m, source.Id)).ToList();
        var toUnskip = selection.Items.Where(m => CanUnskip(m, source.Id)).ToList();

        if (toSkip.Count > 0)
        {
            var text = selection.IsBatch
                ? $"Пропустить (не переносить в {source.TitleFull}) ({toSkip.Count})"
                : $"Пропустить (не переносить в {source.TitleFull})";

            yield return new(text, MenuIcons.Skip)
            {
                Execute = () => RunAsync(toSkip, source, true, ctx),
            };
        }

        if (toUnskip.Count > 0)
        {
            var text = selection.IsBatch
                ? $"Снять пропуск ({source.TitleFull}) ({toUnskip.Count})"
                : $"Снять пропуск ({source.TitleFull})";

            yield return new(text, MenuIcons.Unskip)
            {
                Execute = () => RunAsync(toUnskip, source, false, ctx),
            };
        }
    }

    private static bool CanSkip(Media media, string sourceId)
    {
        var link = media.Sources.FirstOrDefault(s => s.SourceId == sourceId);
        return link is not { Status: MediaStatus.Ok or MediaStatus.PartialOk or MediaStatus.Skipped };
    }

    private static bool CanUnskip(Media media, string sourceId)
    {
        return media.Sources.Any(s => s.SourceId == sourceId && s.Status == MediaStatus.Skipped);
    }

    private static Task RunAsync(IReadOnlyList<Media> mediaList, Source source, bool skip, MediaActionContext ctx)
    {
        var bodyPrefix = skip ? "Помечено пропуском" : "Снят пропуск";
        var errorTitle = skip ? "Пакетная пометка завершена с ошибками" : "Пакетное снятие завершено с ошибками";

        return BatchOperationRunner.RunAsync(mediaList,
            m => m.Title ?? string.Empty,
            (m, _) =>
            {
                if (skip)
                {
                    ctx.Orcestrator.SetSourceSkipped(m, source.Id);
                }
                else
                {
                    ctx.Orcestrator.UnsetSourceSkipped(m, source.Id);
                }

                return Task.CompletedTask;
            },
            bodyPrefix,
            errorTitle,
            ctx.Ui,
            ctx.Logger);
    }
}
