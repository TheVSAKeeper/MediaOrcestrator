using System.Drawing.Text;

namespace MediaOrcestrator.Runner.MediaContextMenu;

internal static class MenuIcons
{
    private static Bitmap? _sync;
    private static Bitmap? _copy;
    private static Bitmap? _delete;
    private static Bitmap? _rename;
    private static Bitmap? _merge;
    private static Bitmap? _convert;
    private static Bitmap? _info;
    private static Bitmap? _open;
    private static Bitmap? _preview;
    private static Bitmap? _skip;
    private static Bitmap? _unskip;

    public static Bitmap? Sync => _sync ??= Create("→", Color.Blue);
    public static Bitmap? Copy => _copy ??= Create("📋", Color.DarkGray);
    public static Bitmap? Delete => _delete ??= Create("✕", Color.Red);
    public static Bitmap? Rename => _rename ??= Create("✎", Color.DarkOrange);
    public static Bitmap? Merge => _merge ??= Create("⊕", Color.Purple);
    public static Bitmap? Convert => _convert ??= Create("↻", Color.Teal);
    public static Bitmap? Info => _info ??= Create("🔍", Color.SteelBlue);
    public static Bitmap? Open => _open ??= Create("↗", Color.DodgerBlue);
    public static Bitmap? Preview => _preview ??= Create("🖼", Color.MediumOrchid);
    public static Bitmap? Skip => _skip ??= Create("⊘", Color.DimGray);
    public static Bitmap? Unskip => _unskip ??= Create("↺", Color.SeaGreen);

    private static Bitmap? Create(string text, Color color)
    {
        try
        {
            var bitmap = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bitmap);
            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using var font = new Font("Segoe UI Symbol", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);

            var size = g.MeasureString(text, font);
            g.DrawString(text, font, brush, (16 - size.Width) / 2, (16 - size.Height) / 2);

            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
