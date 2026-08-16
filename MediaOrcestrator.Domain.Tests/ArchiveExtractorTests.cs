using MediaOrcestrator.Modules;
using System.IO.Compression;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
public class ArchiveExtractorTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "mo-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public void Запись_ведущая_за_пределы_папки_отклоняется()
    {
        var archivePath = CreateArchive("../escape.txt");
        var targetDir = Path.Combine(_root, "extract");
        var escaped = Path.Combine(_root, "escape.txt");

        Assert.That((Action)(() => ArchiveExtractor.Extract(archivePath, targetDir, ArchiveType.SevenZip)), Throws.InstanceOf<InvalidDataException>());
        Assert.That(File.Exists(escaped), Is.False);
    }

    [Test]
    public void Абсолютный_путь_записи_отклоняется()
    {
        var outside = Path.Combine(_root, "outside", "planted.txt");
        var archivePath = CreateArchive(outside);
        var targetDir = Path.Combine(_root, "extract");

        Assert.That((Action)(() => ArchiveExtractor.Extract(archivePath, targetDir, ArchiveType.SevenZip)), Throws.InstanceOf<InvalidDataException>());
        Assert.That(File.Exists(outside), Is.False);
    }

    [Test]
    public void Обычная_вложенная_запись_распаковывается()
    {
        var archivePath = CreateArchive("bin/tool.exe");
        var targetDir = Path.Combine(_root, "extract");

        ArchiveExtractor.Extract(archivePath, targetDir, ArchiveType.SevenZip);

        Assert.That(File.Exists(Path.Combine(targetDir, "bin", "tool.exe")), Is.True);
    }

    private string CreateArchive(string entryKey)
    {
        var archivePath = Path.Combine(_root, "asset.zip");

        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var entry = archive.CreateEntry(entryKey);
        using var entryStream = entry.Open();
        entryStream.Write("payload"u8);

        return archivePath;
    }
}
