namespace MediaOrcestrator.Runner.MediaContextMenu;

public sealed record MenuItemSpec(string Text, Bitmap? Icon)
{
    public bool Enabled { get; init; } = true;
    public string? Tooltip { get; init; }
    public Func<Task>? Execute { get; init; }
}
