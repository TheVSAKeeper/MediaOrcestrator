using SkiaSharp;

namespace MediaOrcestrator.VkVideo;

internal static class ThumbnailCropper
{
    private const double TargetAspectRatio = 9.0 / 16.0;
    private const double AspectTolerance = 0.01;

    public static string CropToShortsIfNeeded(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            return sourcePath;
        }

        using var bitmap = SKBitmap.Decode(sourcePath)
                           ?? throw new InvalidOperationException($"Не удалось декодировать изображение: {sourcePath}");

        var imageHeight = bitmap.Height;
        var imageWidth = bitmap.Width;
        var sourceAspect = (double)imageWidth / imageHeight;
        if (sourceAspect <= TargetAspectRatio + AspectTolerance)
        {
            return sourcePath;
        }

        var targetWidth = (int)Math.Round(imageHeight * TargetAspectRatio);
        if (targetWidth <= 0 || targetWidth >= imageWidth)
        {
            return sourcePath;
        }

        var x = (imageWidth - targetWidth) / 2;
        using var sourceImage = SKImage.FromBitmap(bitmap);
        using var croppedImage = sourceImage.Subset(SKRectI.Create(x, 0, targetWidth, imageHeight))
                                 ?? throw new InvalidOperationException($"Не удалось обрезать изображение: {sourcePath}");

        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
        var croppedPath = Path.Combine(directory, $"{nameWithoutExt}.shorts.jpg");

        using var data = croppedImage.Encode(SKEncodedImageFormat.Jpeg, 100)
                         ?? throw new InvalidOperationException($"Не удалось закодировать изображение в JPEG: {croppedPath}");
        using var stream = File.Create(croppedPath);
        data.SaveTo(stream);

        return croppedPath;
    }
}
