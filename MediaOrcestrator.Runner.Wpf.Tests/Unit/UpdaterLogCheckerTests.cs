using MediaOrcestrator.Runner.Wpf.Bootstrap;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class UpdaterLogCheckerTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MediaOrcestrator.UpdaterLogTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void Check_КогдаЛогОтсутствует_ВозвращаетAbsent()
    {
        var result = UpdaterLogChecker.Check(_tempDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(UpdaterLogResult.Absent));
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.ErrorSummary, Is.Null);
            Assert.That(result.RolledBack, Is.False);
        }
    }

    [Test]
    public void Check_ПриЧистомЛоге_ВозвращаетOkИУдаляетФайл()
    {
        var logPath = Path.Combine(_tempDir, "updater.log");
        File.WriteAllText(logPath, "[12:00:00] Старт обновления\n[12:00:05] Готово\n");

        var result = UpdaterLogChecker.Check(_tempDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(UpdaterLogResult.Ok));
            Assert.That(result.HasErrors, Is.False);
            Assert.That(File.Exists(logPath), Is.False);
        }
    }

    [Test]
    public void Check_ПриОшибкахСПрефиксомДублямиИДлиннойСтрокой_ВозвращаетСокращённуюСводкуИУдаляетФайл()
    {
        var logPath = Path.Combine(_tempDir, "updater.log");
        var longLine = new string('а', 250);
        File.WriteAllText(logPath, string.Join('\n',
            "[12:00:00] Старт",
            "[12:00:01] ОШИБКА: Первая ошибка",
            "[12:00:02] ОШИБКА: Первая ошибка",
            $"[12:00:03] ОШИБКА: {longLine}",
            "[12:00:04] ОШИБКА: Третья ошибка"));

        var result = UpdaterLogChecker.Check(_tempDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(UpdaterLogResult.HasErrors));
            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.RolledBack, Is.False);
            Assert.That(result.ErrorSummary, Is.Not.Null);
            Assert.That(result.ErrorSummary!, Does.StartWith("Первая ошибка\n"));
            Assert.That(result.ErrorSummary!, Does.EndWith("…"));
            Assert.That(result.ErrorSummary!, Does.Not.Contain("Третья ошибка"));
            Assert.That(result.ErrorSummary!.Length, Is.LessThanOrEqualTo(220));
            Assert.That(File.Exists(logPath), Is.False);
        }
    }

    [Test]
    public void Check_ПриОткатеИОшибкеОтката_ВозвращаетRolledBackИПрефиксОтката()
    {
        var logPath = Path.Combine(_tempDir, "updater.log");
        File.WriteAllText(logPath, string.Join('\n',
            "[12:00:01] ОШИБКА: Копирование не удалось",
            "[12:00:02] Откат завершён.",
            "[12:00:03] ОШИБКА ОТКАТА: Не удалось удалить временный файл"));

        var result = UpdaterLogChecker.Check(_tempDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(UpdaterLogResult.HasErrors));
            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.RolledBack, Is.True);
            Assert.That(result.ErrorSummary, Is.EqualTo("Копирование не удалось\nОткат: Не удалось удалить временный файл"));
            Assert.That(File.Exists(logPath), Is.False);
        }
    }
}
