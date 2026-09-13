using CommunityToolkit.Mvvm.ComponentModel;
using MediaOrcestrator.Domain;
using SkiaSharp;
using System.Globalization;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class CoverTextLayerViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    private string _textTemplate = "{number}";

    [ObservableProperty]
    private double _textXPercent = 50;

    [ObservableProperty]
    private double _textYPercent = 50;

    [ObservableProperty]
    private double _fontSizePercent = 25;

    [ObservableProperty]
    private string _fontFamily = "Impact";

    [ObservableProperty]
    private CoverFontStyle _fontStyle = CoverFontStyle.Bold;

    [ObservableProperty]
    private double _strokeWidthPercent = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FillBrush))]
    private string _fillColorHex = "FFFFFFFF";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrokeBrush))]
    private string _strokeColorHex = "FF000000";

    public string DisplayLabel => string.IsNullOrWhiteSpace(TextTemplate) ? "(пустой слой)" : TextTemplate;

    public Brush FillBrush => MakeBrush(FillColorHex, Colors.White);

    public Brush StrokeBrush => MakeBrush(StrokeColorHex, Colors.Black);

    public static CoverTextLayerViewModel FromDomain(CoverTextLayer layer)
    {
        return new()
        {
            TextTemplate = layer.TextTemplate,
            TextXPercent = Math.Round(layer.TextX * 100, 1),
            TextYPercent = Math.Round(layer.TextY * 100, 1),
            FontSizePercent = Math.Round(layer.FontSizeRatio * 100, 1),
            FontFamily = layer.FontFamily,
            FontStyle = layer.FontStyle,
            StrokeWidthPercent = Math.Round(layer.StrokeWidthRatio * 100, 2),
            FillColorHex = ((uint)layer.FillColor).ToString("X8", CultureInfo.InvariantCulture),
            StrokeColorHex = ((uint)layer.StrokeColor).ToString("X8", CultureInfo.InvariantCulture),
        };
    }

    public CoverTextLayer ToDomain()
    {
        return new(TextTemplate,
            (float)Math.Clamp(TextXPercent / 100.0, 0, 1),
            (float)Math.Clamp(TextYPercent / 100.0, 0, 1),
            (float)Math.Clamp(FontSizePercent / 100.0, 0, 1),
            string.IsNullOrWhiteSpace(FontFamily) ? "Impact" : FontFamily,
            FontStyle,
            ToSkColor(FillColorHex, new SKColor(255, 255, 255)),
            ToSkColor(StrokeColorHex, new SKColor(0, 0, 0)),
            (float)Math.Clamp(StrokeWidthPercent / 100.0, 0, 1));
    }

    private static Brush MakeBrush(string hex, Color fallback)
    {
        var color = ParseColor(hex) ?? fallback;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static SKColor ToSkColor(string hex, SKColor fallback)
    {
        return TryParseArgb(hex, out var argb) ? new SKColor(argb) : fallback;
    }

    private static Color? ParseColor(string hex)
    {
        if (!TryParseArgb(hex, out var argb))
        {
            return null;
        }

        return Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));
    }

    private static bool TryParseArgb(string? hex, out uint argb)
    {
        argb = 0;
        var value = hex?.Trim().TrimStart('#');

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value.Length == 6 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            argb = 0xFF000000u | rgb;
            return true;
        }

        return value.Length == 8 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out argb);
    }
}
