using MediaOrcestrator.Rutube;
using MediaOrcestrator.VkVideo;
using SkiaSharp;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
file sealed class PluginThumbnailTests
{
    private string _tempDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PluginThumbTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    [Test]
    public void ConvertWebPToJpeg_при_валидном_webp_создаёт_jpeg_с_теми_же_размерами()
    {
        var webpPath = Path.Combine(_tempDir, "thumb.webp");
        SaveImage(webpPath, 640, 360, SKEncodedImageFormat.Webp, SKColors.CornflowerBlue);

        var jpgPath = RutubeService.ConvertWebPToJpeg(webpPath);

        using var codec = SKCodec.Create(jpgPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(jpgPath, Is.EqualTo(webpPath + ".jpg"));
            Assert.That(File.Exists(jpgPath), Is.True);
            Assert.That(codec, Is.Not.Null);
            Assert.That(codec!.EncodedFormat, Is.EqualTo(SKEncodedImageFormat.Jpeg));
            Assert.That(codec.Info.Width, Is.EqualTo(640));
            Assert.That(codec.Info.Height, Is.EqualTo(360));
        }
    }

    [Test]
    public void ConvertWebPToJpeg_при_битом_файле_бросает_InvalidOperationException()
    {
        var badPath = Path.Combine(_tempDir, "broken.webp");
        File.WriteAllText(badPath, "not an image");

        Exception? caught = null;
        try
        {
            RutubeService.ConvertWebPToJpeg(badPath);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.That(caught, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void CropToShortsIfNeeded_широкое_изображение_обрезается_по_центру_в_9_к_16()
    {
        var sourcePath = Path.Combine(_tempDir, "wide.png");
        CreateCenterMarkedImage(sourcePath, 1920, 1080);

        var croppedPath = ThumbnailCropper.CropToShortsIfNeeded(sourcePath);

        using var codec = SKCodec.Create(croppedPath);
        using var decoded = SKBitmap.Decode(croppedPath);
        var centerPixel = decoded.GetPixel(decoded.Width / 2, decoded.Height / 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(croppedPath, Is.EqualTo(Path.Combine(_tempDir, "wide.shorts.jpg")));
            Assert.That(codec!.EncodedFormat, Is.EqualTo(SKEncodedImageFormat.Jpeg));
            Assert.That(decoded.Width, Is.EqualTo(608));
            Assert.That(decoded.Height, Is.EqualTo(1080));
            Assert.That(centerPixel.Green, Is.GreaterThan(200));
            Assert.That(centerPixel.Red, Is.LessThan(50));
        }
    }

    [TestCase(720, 1280)]
    [TestCase(608, 1080)]
    public void CropToShortsIfNeeded_портретное_изображение_возвращает_исходный_путь(int width, int height)
    {
        var sourcePath = Path.Combine(_tempDir, $"portrait_{width}x{height}.png");
        SaveImage(sourcePath, width, height, SKEncodedImageFormat.Png, SKColors.White);

        var result = ThumbnailCropper.CropToShortsIfNeeded(sourcePath);

        Assert.That(result, Is.EqualTo(sourcePath));
    }

    [Test]
    public void CropToShortsIfNeeded_несуществующий_файл_возвращает_исходный_путь()
    {
        var missingPath = Path.Combine(_tempDir, "missing.png");

        var result = ThumbnailCropper.CropToShortsIfNeeded(missingPath);

        Assert.That(result, Is.EqualTo(missingPath));
    }

    [Test]
    public void CropToShortsIfNeeded_при_битом_файле_бросает_InvalidOperationException()
    {
        var badPath = Path.Combine(_tempDir, "broken.png");
        File.WriteAllText(badPath, "not an image");

        Exception? caught = null;
        try
        {
            ThumbnailCropper.CropToShortsIfNeeded(badPath);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.That(caught, Is.TypeOf<InvalidOperationException>());
    }

    private static void SaveImage(string path, int width, int height, SKEncodedImageFormat format, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static void CreateCenterMarkedImage(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Red);
        using var paint = new SKPaint { Color = SKColors.Lime };
        var stripWidth = 200;
        canvas.DrawRect((width - stripWidth) / 2f, 0, stripWidth, height, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
