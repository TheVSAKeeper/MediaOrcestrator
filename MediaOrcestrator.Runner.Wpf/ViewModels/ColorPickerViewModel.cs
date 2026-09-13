using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class ColorPickerViewModel : ObservableObject
{
    private bool _suppressSync;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBrush))]
    private double _alpha = 255;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBrush))]
    private double _red = 255;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBrush))]
    private double _green = 255;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBrush))]
    private double _blue = 255;

    [ObservableProperty]
    private string _hex = "FFFFFFFF";

    public ColorPickerViewModel(string title, string? initialHex)
    {
        Title = title;

        _suppressSync = true;
        try
        {
            if (TryParseArgb(initialHex, out var a, out var r, out var g, out var b))
            {
                Alpha = a;
                Red = r;
                Green = g;
                Blue = b;
            }

            Hex = FormatHex();
        }
        finally
        {
            _suppressSync = false;
        }

        Presets =
        [
            new("Белый", "FFFFFFFF"),
            new("Чёрный", "FF000000"),
            new("Красный", "FFE53935"),
            new("Оранжевый", "FFFB8C00"),
            new("Жёлтый", "FFFDD835"),
            new("Зелёный", "FF43A047"),
            new("Синий", "FF1E88E5"),
            new("Фиолетовый", "FF8E24AA"),
        ];
    }

    public event EventHandler? CloseRequested;

    public string Title { get; }

    public IReadOnlyList<ColorPresetOption> Presets { get; }

    public Brush PreviewBrush
    {
        get
        {
            var brush = new SolidColorBrush(Color.FromArgb(ToByte(Alpha), ToByte(Red), ToByte(Green), ToByte(Blue)));
            brush.Freeze();
            return brush;
        }
    }

    [RelayCommand]
    private void ApplyPreset(string? hex)
    {
        if (!TryParseArgb(hex, out var a, out var r, out var g, out var b))
        {
            return;
        }

        _suppressSync = true;
        try
        {
            Alpha = a;
            Red = r;
            Green = g;
            Blue = b;
        }
        finally
        {
            _suppressSync = false;
        }

        Hex = FormatHex();
    }

    [RelayCommand]
    private void Done() => CloseRequested?.Invoke(this, EventArgs.Empty);

    partial void OnAlphaChanged(double value) => SyncHexFromComponents();

    partial void OnRedChanged(double value) => SyncHexFromComponents();

    partial void OnGreenChanged(double value) => SyncHexFromComponents();

    partial void OnBlueChanged(double value) => SyncHexFromComponents();

    partial void OnHexChanged(string value)
    {
        if (_suppressSync || !TryParseArgb(value, out var a, out var r, out var g, out var b))
        {
            return;
        }

        _suppressSync = true;
        try
        {
            Alpha = a;
            Red = r;
            Green = g;
            Blue = b;
        }
        finally
        {
            _suppressSync = false;
        }
    }

    private void SyncHexFromComponents()
    {
        if (_suppressSync)
        {
            return;
        }

        _suppressSync = true;
        try
        {
            Hex = FormatHex();
        }
        finally
        {
            _suppressSync = false;
        }
    }

    private string FormatHex()
    {
        var argb = ((uint)ToByte(Alpha) << 24)
            | ((uint)ToByte(Red) << 16)
            | ((uint)ToByte(Green) << 8)
            | ToByte(Blue);

        return argb.ToString("X8", CultureInfo.InvariantCulture);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    private static bool TryParseArgb(string? hex, out double a, out double r, out double g, out double b)
    {
        a = r = g = b = 0;
        var value = hex?.Trim().TrimStart('#');

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        uint argb;

        if (value.Length == 6 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            argb = 0xFF000000u | rgb;
        }
        else if (value.Length == 8 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var full))
        {
            argb = full;
        }
        else
        {
            return false;
        }

        a = (argb >> 24) & 0xFF;
        r = (argb >> 16) & 0xFF;
        g = (argb >> 8) & 0xFF;
        b = argb & 0xFF;
        return true;
    }
}

public sealed class ColorPresetOption(string name, string hex)
{
    public string Name { get; } = name;

    public string Hex { get; } = hex;

    public Brush Brush { get; } = BuildBrush(hex);

    private static Brush BuildBrush(string hex)
    {
        var value = hex.TrimStart('#');
        var argb = uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF)));
        brush.Freeze();
        return brush;
    }
}
