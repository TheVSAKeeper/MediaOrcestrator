namespace MediaOrcestrator.Runner.MediaContextMenu;

public interface IMediaMenuAction
{
    int Order { get; }

    IEnumerable<MenuItemSpec> Build(MediaSelection selection, MediaActionContext ctx);
}

public interface IAsyncMediaMenuAction : IMediaMenuAction
{
    string LoadingPlaceholder { get; }

    Bitmap? LoadingIcon { get; }

    Task<IEnumerable<MenuItemSpec>> BuildAsync(MediaSelection selection, MediaActionContext ctx, CancellationToken ct);
}
