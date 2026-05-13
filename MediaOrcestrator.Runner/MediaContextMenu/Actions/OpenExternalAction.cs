using MediaOrcestrator.Modules;
using System.Diagnostics;

namespace MediaOrcestrator.Runner.MediaContextMenu.Actions;

internal sealed class OpenExternalAction : IMediaMenuAction
{
    public int Order => 650;

    public IEnumerable<MenuItemSpec> Build(MediaSelection selection, MediaActionContext ctx)
    {
        if (selection.IsBatch)
        {
            yield break;
        }

        var media = selection.First;
        var allSources = ctx.Orcestrator.GetSources();

        var relevantLinks = (selection.SpecificSource != null
                ? media.Sources.Where(s => s.SourceId == selection.SpecificSource.Id)
                : media.Sources)
            .Where(s => s.Status != MediaStatus.Skipped && !string.IsNullOrEmpty(s.ExternalId))
            .ToList();

        foreach (var link in relevantLinks)
        {
            var source = allSources.FirstOrDefault(x => x.Id == link.SourceId);

            if (source?.Type == null)
            {
                continue;
            }

            var metadata = media.Metadata.ForSource(source.Id);
            var uri = source.Type.GetExternalUri(link.ExternalId, source.Settings, metadata);

            if (uri == null)
            {
                continue;
            }

            yield return new($"Открыть: {source.TitleFull}", MenuIcons.Open)
            {
                Execute = () =>
                {
                    Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
                    return Task.CompletedTask;
                },
            };
        }
    }
}
