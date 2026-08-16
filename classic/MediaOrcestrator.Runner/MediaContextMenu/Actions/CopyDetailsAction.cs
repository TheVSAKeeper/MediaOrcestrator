using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using System.Text;

namespace MediaOrcestrator.Runner.MediaContextMenu.Actions;

internal sealed class CopyDetailsAction : IMediaMenuAction
{
    public int Order => 600;

    public IEnumerable<MenuItemSpec> Build(MediaSelection selection, MediaActionContext ctx)
    {
        if (selection.IsBatch)
        {
            yield break;
        }

        yield return new("Копировать детали в буфер обмена", MenuIcons.Copy)
        {
            Execute = () => Run(selection.First, ctx),
        };
    }

    private static Task Run(Media media, MediaActionContext ctx)
    {
        try
        {
            var sources = ctx.Orcestrator.GetSources();
            var details = new StringBuilder();

            details.AppendLine($"Название: {media.Title}");

            if (!string.IsNullOrEmpty(media.Description))
            {
                details.AppendLine($"Описание: {media.Description}");
            }

            if (media.Metadata.Count > 0)
            {
                details.AppendLine();
                details.AppendLine("Метаданные:");
                foreach (var meta in media.Metadata)
                {
                    details.AppendLine($"  {meta.Key}: {meta.Value}");
                }
            }

            details.AppendLine();
            details.AppendLine("Источники:");

            if (media.Sources.Count == 0)
            {
                details.AppendLine(" Нет источников");
            }
            else
            {
                foreach (var sourceLink in media.Sources)
                {
                    var source = sources.FirstOrDefault(s => s.Id == sourceLink.SourceId);
                    var sourceName = source?.TitleFull ?? "Неизвестный источник";
                    var status = MediaStatusHelper.GetById(sourceLink.Status);

                    details.AppendLine($"  {sourceName}: {status.IconText + " " + status.Text}");

                    if (!string.IsNullOrEmpty(sourceLink.ExternalId))
                    {
                        details.AppendLine($"    ID: {sourceLink.ExternalId}");
                    }
                }
            }

            Clipboard.SetText(details.ToString());
            MessageBox.Show(ctx.Ui.Owner,
                "Детали медиа скопированы в буфер обмена",
                "Успех",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ctx.Ui.Owner,
                $"Ошибка при копировании: {ex.Message}",
                "Ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        return Task.CompletedTask;
    }
}
