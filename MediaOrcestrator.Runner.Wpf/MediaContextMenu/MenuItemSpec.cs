namespace MediaOrcestrator.Runner.Wpf.MediaContextMenu;

public sealed record MenuItemSpec(string Text)
{
    public MenuGlyph? Glyph { get; init; }

    public bool Enabled { get; init; } = true;

    public string? Tooltip { get; init; }

    public Func<Task>? Execute { get; init; }

    public IReadOnlyList<MenuItemSpec>? Children { get; init; }
}
