using SkiaSharp;
using System.IO;
using System.Windows.Media.Imaging;

namespace MediaOrcestrator.Runner.Wpf.Services;

public static class SkiaImaging
{
    public static BitmapSource ToBitmapSource(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);

        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;

        var result = new BitmapImage();
        result.BeginInit();
        result.CacheOption = BitmapCacheOption.OnLoad;
        result.StreamSource = stream;
        result.EndInit();
        result.Freeze();

        return result;
    }
}
