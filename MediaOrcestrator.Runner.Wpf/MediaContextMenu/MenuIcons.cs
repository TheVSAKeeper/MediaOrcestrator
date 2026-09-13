using MahApps.Metro.IconPacks;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.MediaContextMenu;

internal static class MenuIcons
{
    public static MenuGlyph Sync { get; } = Make(Color.FromRgb(33, 99, 200), PackIconLucideKind.ArrowRight);
    public static MenuGlyph MetadataRefresh { get; } = Make(Color.FromRgb(46, 160, 140), PackIconLucideKind.RefreshCcw);
    public static MenuGlyph Copy { get; } = Make(Color.FromRgb(120, 120, 120), PackIconLucideKind.Copy);
    public static MenuGlyph Delete { get; } = Make(Color.FromRgb(208, 64, 64), PackIconLucideKind.Trash2);
    public static MenuGlyph Rename { get; } = Make(Color.FromRgb(220, 130, 32), PackIconLucideKind.Pencil);
    public static MenuGlyph Merge { get; } = Make(Color.FromRgb(140, 80, 180), PackIconLucideKind.Merge);
    public static MenuGlyph Convert { get; } = Make(Color.FromRgb(48, 152, 152), PackIconLucideKind.RefreshCw);
    public static MenuGlyph Info { get; } = Make(Color.FromRgb(70, 124, 184), PackIconLucideKind.Search);
    public static MenuGlyph Open { get; } = Make(Color.FromRgb(56, 132, 230), PackIconLucideKind.ExternalLink);
    public static MenuGlyph Preview { get; } = Make(Color.FromRgb(168, 84, 200), PackIconLucideKind.Image);
    public static MenuGlyph Skip { get; } = Make(Color.FromRgb(96, 96, 96), PackIconLucideKind.Ban);
    public static MenuGlyph Unskip { get; } = Make(Color.FromRgb(48, 156, 96), PackIconLucideKind.RotateCcw);

    private static MenuGlyph Make(Color color, PackIconLucideKind icon)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        return new(brush, icon);
    }
}

public sealed record MenuGlyph(Brush Brush, PackIconLucideKind Icon);
