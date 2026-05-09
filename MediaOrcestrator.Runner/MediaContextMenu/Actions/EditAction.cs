namespace MediaOrcestrator.Runner.MediaContextMenu.Actions;

internal sealed class EditAction : IMediaMenuAction
{
    public int Order => 500;

    public IEnumerable<MenuItemSpec> Build(MediaSelection selection, MediaActionContext ctx)
    {
        var renameText = selection.IsBatch
            ? $"Пакетное переименование ({selection.Count})..."
            : "Переименовать...";

        yield return new(renameText, MenuIcons.Rename)
        {
            Execute = () => RunRename(selection, ctx),
        };

        var previewText = selection.IsBatch
            ? $"Обновить превью ({selection.Count})..."
            : "Обновить превью...";

        yield return new(previewText, MenuIcons.Preview)
        {
            Execute = () => RunPreview(selection, ctx),
        };

        if (selection.Count >= 2)
        {
            yield return new($"Объединить ({selection.Count})", MenuIcons.Merge)
            {
                Execute = () =>
                {
                    MergeRunner.Run(selection.InSelectionOrder, ctx);
                    return Task.CompletedTask;
                },
            };
        }
    }

    private static Task RunRename(MediaSelection selection, MediaActionContext ctx)
    {
        var medias = selection.Items.ToList();
        using var form = new BatchRenameForm(medias, ctx.BatchRenameService);

        if (!selection.IsBatch)
        {
            form.Text = $"Переименование «{medias[0].Title}»";
        }

        if (form.ShowDialog(ctx.Ui.Owner) == DialogResult.OK)
        {
            ctx.Ui.NotifyDataChanged();
        }

        return Task.CompletedTask;
    }

    private static Task RunPreview(MediaSelection selection, MediaActionContext ctx)
    {
        var medias = selection.Items.ToList();
        using var form = new BatchPreviewForm(medias, ctx.BatchPreviewService, ctx.CoverGenerator, ctx.CoverTemplateStore);

        if (!selection.IsBatch)
        {
            form.Text = $"Обновление превью «{medias[0].Title}»";
        }

        if (form.ShowDialog(ctx.Ui.Owner) == DialogResult.OK)
        {
            ctx.Ui.NotifyDataChanged();
        }

        return Task.CompletedTask;
    }
}
