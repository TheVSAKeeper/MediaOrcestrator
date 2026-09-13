using MediaOrcestrator.Runner.Wpf.Services;

namespace MediaOrcestrator.Runner.Wpf.MediaContextMenu.Actions;

internal sealed class EditAction : IMediaMenuAction
{
    public int Order => 500;

    public IEnumerable<MenuItemSpec> Build(MediaSelection selection, MediaActionContext ctx)
    {
        var renameText = selection.IsBatch
            ? $"Пакетное переименование ({selection.Count})..."
            : "Переименовать...";

        yield return new(renameText)
        {
            Glyph = MenuIcons.Rename,
            Execute = () => Navigate(OperationsTab.Rename, selection, ctx),
        };

        var previewText = selection.IsBatch
            ? $"Обновить превью ({selection.Count})..."
            : "Обновить превью...";

        yield return new(previewText)
        {
            Glyph = MenuIcons.Preview,
            Execute = () => Navigate(OperationsTab.Preview, selection, ctx),
        };

        if (selection.Count >= 2)
        {
            yield return new($"Объединить ({selection.Count})")
            {
                Glyph = MenuIcons.Merge,
                Execute = () => MergeRunner.RunAsync(selection.InSelectionOrder, ctx),
            };
        }
    }

    private static Task Navigate(OperationsTab tab, MediaSelection selection, MediaActionContext ctx)
    {
        ctx.Navigation.OpenOperations(tab, selection.Items);
        return Task.CompletedTask;
    }
}
