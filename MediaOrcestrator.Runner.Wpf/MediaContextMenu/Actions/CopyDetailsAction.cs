using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MediaOrcestrator.Runner.Wpf.MediaContextMenu.Actions;

internal sealed class CopyDetailsAction(IClipboardService clipboard) : IMediaMenuAction
{
    public int Order => 600;

    public IEnumerable<MenuItemSpec> Build(MediaSelection selection, MediaActionContext ctx)
    {
        if (selection.IsBatch)
        {
            yield break;
        }

        yield return new("Копировать детали в буфер обмена")
        {
            Glyph = MenuIcons.Copy,
            Execute = () => Run(selection.First, ctx),
        };
    }

    private Task Run(Media media, MediaActionContext ctx)
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

            if (clipboard.TrySetText(details.ToString()))
            {
                ctx.Dialogs.Info("Успех", "Детали медиа скопированы в буфер обмена");
            }
            else
            {
                ctx.Dialogs.Error("Ошибка", "Не удалось положить детали медиа в буфер обмена");
            }
        }
        catch (Exception ex)
        {
            ctx.LoggerFactory.CreateLogger<CopyDetailsAction>().LogError(ex, "Не удалось скопировать детали медиа {MediaId}", media.Id);
            ctx.Dialogs.Error("Ошибка", $"Ошибка при копировании: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}
