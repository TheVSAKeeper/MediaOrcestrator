using System.Security.Cryptography;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
public class FileIntegrityTests
{
    private string _dir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mo-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Test]
    public void Совпадающая_контрольная_сумма_подтверждает_файл()
    {
        var path = WriteFile("payload");

        Assert.That(FileIntegrity.EnsureMatches(path, "sha256:" + Sha256Of(path)), Is.True);
    }

    [Test]
    public void Несовпадающая_контрольная_сумма_отклоняет_файл()
    {
        var path = WriteFile("payload");
        var alien = new string('a', 64);

        Assert.That((Action)(() => FileIntegrity.EnsureMatches(path, "sha256:" + alien)), Throws.InstanceOf<InvalidDataException>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("md5:0123456789abcdef0123456789abcdef")]
    [TestCase("sha256:слишкомкороткая")]
    public void Непригодная_контрольная_сумма_означает_проверку_не_проводили(string? digest)
    {
        var path = WriteFile("payload");

        Assert.That(FileIntegrity.EnsureMatches(path, digest), Is.False);
    }

    [Test]
    public void Регистр_шестнадцатеричной_записи_не_влияет_на_сверку()
    {
        var path = WriteFile("payload");

        Assert.That(FileIntegrity.EnsureMatches(path, "sha256:" + Sha256Of(path).ToLowerInvariant()), Is.True);
    }

    private string WriteFile(string content)
    {
        var path = Path.Combine(_dir, "asset.bin");
        File.WriteAllText(path, content);

        return path;
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();

        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}
