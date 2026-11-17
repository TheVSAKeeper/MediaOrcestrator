using LiteDB;
using MediaOrcestrator.Domain.Migration;
using NSubstitute;

namespace MediaOrcestrator.Domain.Tests.Migration;

[TestFixture]
public sealed class WinFormsInstallationInspectorTests
{
    private string _rootDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "mo-inspector-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_rootDir))
        {
            Directory.Delete(_rootDir, true);
        }
    }

    [Test]
    public void Inspect_валидная_установка_0_0_5_9_читает_сводку_и_не_меняет_sha256_базы_и_журнала()
    {
        var sourceDir = CreateWinFormsFolder("wf-valid", dbFileName: "my catalog.db", stateDirName: "my state", withWal: true);
        var currentWpfDir = Path.Combine(_rootDir, "wpf-current");
        Directory.CreateDirectory(currentWpfDir);

        var dbPath = Path.Combine(sourceDir, "my catalog.db");
        var walPath = WinFormsProcessDetector.GetWalLogPath(dbPath);
        var dbShaBefore = FileIntegrity.ComputeSha256(dbPath);
        var walShaBefore = FileIntegrity.ComputeSha256(walPath);

        var detector = Substitute.For<IWinFormsProcessDetector>();
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>()).Returns(WinFormsProcessStatus.Free);

        var inspector = new WinFormsInstallationInspector(detector, _ => new Version(0, 0, 5, 9));

        var result = inspector.Inspect(sourceDir, currentWpfDir);

        var dbShaAfter = FileIntegrity.ComputeSha256(dbPath);
        var walShaAfter = FileIntegrity.ComputeSha256(walPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.IssueCode, Is.EqualTo(WinFormsInspectionIssueCode.None));
            Assert.That(result.DetectedVersion, Is.EqualTo(new Version(0, 0, 5, 9)));
            Assert.That(result.Settings, Is.Not.Null);
            Assert.That(result.Settings!.DatabasePath, Is.EqualTo(Path.GetFullPath(dbPath)));
            Assert.That(result.Settings.StatePath, Is.EqualTo(Path.GetFullPath(Path.Combine(sourceDir, "my state"))));
            Assert.That(result.Summary, Is.Not.Null);
            Assert.That(result.Summary!.SourcesCount, Is.EqualTo(2));
            Assert.That(result.Summary.MediasCount, Is.EqualTo(1));
            Assert.That(result.Summary.MediaLinksCount, Is.EqualTo(2));
            Assert.That(result.Summary.RelationsCount, Is.EqualTo(1));
            Assert.That(result.Summary.CoverTemplatesCount, Is.EqualTo(1));
            Assert.That(result.Summary.HasWalLog, Is.True);
            Assert.That(dbShaAfter, Is.EqualTo(dbShaBefore));
            Assert.That(walShaAfter, Is.EqualTo(walShaBefore));
        }
    }

    [TestCase("0.0.5.8")]
    [TestCase("0.0.5.3")]
    [TestCase(null)]
    public void Inspect_устаревшая_или_неизвестная_версия_возвращает_UnsupportedVersion(string? rawVersion)
    {
        var sourceDir = CreateWinFormsFolder("wf-old");
        var currentWpfDir = Path.Combine(_rootDir, "wpf-current");
        Directory.CreateDirectory(currentWpfDir);

        var detector = Substitute.For<IWinFormsProcessDetector>();
        Version? parsed = rawVersion is null ? null : Version.Parse(rawVersion);
        var inspector = new WinFormsInstallationInspector(detector, _ => parsed);

        var result = inspector.Inspect(sourceDir, currentWpfDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.IssueCode, Is.EqualTo(WinFormsInspectionIssueCode.UnsupportedVersion));
            Assert.That(result.ErrorMessage, Does.Contain("0.0.5.9"));
        }
    }

    [Test]
    public void Inspect_текущая_папка_wpf_отклоняется_как_SelfTarget()
    {
        var currentWpfDir = CreateWinFormsFolder("wpf-self");
        var detector = Substitute.For<IWinFormsProcessDetector>();
        var inspector = new WinFormsInstallationInspector(detector, _ => new Version(0, 0, 5, 9));

        var result = inspector.Inspect(currentWpfDir, currentWpfDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.IssueCode, Is.EqualTo(WinFormsInspectionIssueCode.SelfTarget));
        }
    }

    [Test]
    public void Inspect_папка_wpf_без_классического_раннера_отклоняется_как_WpfInstallation()
    {
        var wpfDir = Path.Combine(_rootDir, "other-wpf");
        Directory.CreateDirectory(wpfDir);
        File.WriteAllText(Path.Combine(wpfDir, "MediaOrcestrator.Runner.Wpf.dll"), string.Empty);

        var currentWpfDir = Path.Combine(_rootDir, "wpf-current");
        Directory.CreateDirectory(currentWpfDir);

        var detector = Substitute.For<IWinFormsProcessDetector>();
        var inspector = new WinFormsInstallationInspector(detector, _ => new Version(0, 0, 5, 9));

        var result = inspector.Inspect(wpfDir, currentWpfDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.IssueCode, Is.EqualTo(WinFormsInspectionIssueCode.WpfInstallation));
        }
    }

    [Test]
    public void Inspect_отсутствие_файла_базы_отклоняется_как_MissingDatabase()
    {
        var sourceDir = CreateWinFormsFolder("wf-nodb");
        File.Delete(Path.Combine(sourceDir, "MyData.db"));

        var currentWpfDir = Path.Combine(_rootDir, "wpf-current");
        Directory.CreateDirectory(currentWpfDir);

        var detector = Substitute.For<IWinFormsProcessDetector>();
        var inspector = new WinFormsInstallationInspector(detector, _ => new Version(0, 0, 5, 9));

        var result = inspector.Inspect(sourceDir, currentWpfDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.IssueCode, Is.EqualTo(WinFormsInspectionIssueCode.MissingDatabase));
        }
    }

    [Test]
    public void Inspect_маркер_отложенного_восстановления_отклоняется_как_PendingDatabaseRestore()
    {
        var sourceDir = CreateWinFormsFolder("wf-restore-pending");
        File.WriteAllText(Path.Combine(sourceDir, "MyData.restore-pending"), "MyData_backup.db");

        var currentWpfDir = Path.Combine(_rootDir, "wpf-current");
        Directory.CreateDirectory(currentWpfDir);

        var detector = Substitute.For<IWinFormsProcessDetector>();
        var inspector = new WinFormsInstallationInspector(detector, _ => new Version(0, 0, 5, 9));

        var result = inspector.Inspect(sourceDir, currentWpfDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.IssueCode, Is.EqualTo(WinFormsInspectionIssueCode.PendingDatabaseRestore));
        }
    }

    [Test]
    public void Inspect_отсутствие_settings_txt_отклоняется_как_MissingSettingsFile()
    {
        var sourceDir = CreateWinFormsFolder("wf-nosettings");
        File.Delete(Path.Combine(sourceDir, "settings.txt"));

        var currentWpfDir = Path.Combine(_rootDir, "wpf-current");
        Directory.CreateDirectory(currentWpfDir);

        var detector = Substitute.For<IWinFormsProcessDetector>();
        var inspector = new WinFormsInstallationInspector(detector, _ => new Version(0, 0, 5, 9));

        var result = inspector.Inspect(sourceDir, currentWpfDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.IssueCode, Is.EqualTo(WinFormsInspectionIssueCode.MissingSettingsFile));
        }
    }

    [Test]
    public void Inspect_при_занятом_процессе_возвращает_IsBusy_без_чтения_сводки_бд()
    {
        var sourceDir = CreateWinFormsFolder("wf-busy");
        var currentWpfDir = Path.Combine(_rootDir, "wpf-current");
        Directory.CreateDirectory(currentWpfDir);

        var detector = Substitute.For<IWinFormsProcessDetector>();
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new WinFormsProcessStatus(true, true, [4242], false, false));

        var inspector = new WinFormsInstallationInspector(detector, _ => new Version(0, 0, 5, 9));

        var result = inspector.Inspect(sourceDir, currentWpfDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ProcessStatus.IsBusy, Is.True);
            Assert.That(result.ProcessStatus.RunningProcessIds, Contains.Item(4242));
            Assert.That(result.Summary, Is.Null);
        }
    }

    [Test]
    public void LegacySettingsParser_разбирает_относительные_и_абсолютные_пути_с_пробелами()
    {
        var sourceDir = Path.Combine(_rootDir, "wf-parse");
        Directory.CreateDirectory(sourceDir);
        var absTemp = Path.Combine(_rootDir, "external temp dir");

        var parsed = LegacySettingsParser.ParseLines(
        [
            "",
            "database_path sub folder\\my data.db",
            $"temp_path {absTemp}",
            "state_path custom state",
        ]);

        var snapshot = LegacySettingsParser.ResolveSnapshot(sourceDir, parsed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.DatabasePath, Is.EqualTo(Path.GetFullPath(Path.Combine(sourceDir, "sub folder", "my data.db"))));
            Assert.That(snapshot.TempPath, Is.EqualTo(Path.GetFullPath(absTemp)));
            Assert.That(snapshot.StatePath, Is.EqualTo(Path.GetFullPath(Path.Combine(sourceDir, "custom state"))));
            Assert.That(snapshot.PluginPath, Is.EqualTo(Path.GetFullPath(Path.Combine(sourceDir, "ModuleBuilds"))));
        }
    }

    [Test]
    public void LegacySettingsParser_снимает_кавычки_и_отклоняет_относительный_путь_с_буквой_диска()
    {
        var sourceDir = Path.Combine(_rootDir, "wf-quoted");
        Directory.CreateDirectory(sourceDir);

        var quotedExternal = Path.Combine(_rootDir, "external dir", "MyData.db");
        var raw = LegacySettingsParser.ParseLines(
        [
            $"database_path \"{quotedExternal}\"",
            "state_path \"custom state\"",
        ]);

        var snapshot = LegacySettingsParser.ResolveSnapshot(sourceDir, raw);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.DatabasePath, Is.EqualTo(Path.GetFullPath(quotedExternal)));
            Assert.That(snapshot.StatePath, Is.EqualTo(Path.GetFullPath(Path.Combine(sourceDir, "custom state"))));
        }

        FormatException? driveEx = null;
        try
        {
            LegacySettingsParser.ResolvePath(sourceDir, "D:relative.db", "MyData.db");
        }
        catch (FormatException ex)
        {
            driveEx = ex;
        }

        Assert.That(driveEx, Is.Not.Null);
    }

    private string CreateWinFormsFolder(
        string name,
        string dbFileName = "MyData.db",
        string stateDirName = "state",
        bool withWal = false)
    {
        var dir = Path.Combine(_rootDir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "MediaOrcestrator.Runner.dll"), string.Empty);
        File.WriteAllText(Path.Combine(dir, "MediaOrcestrator.Runner.exe"), string.Empty);
        File.WriteAllLines(Path.Combine(dir, "settings.txt"),
        [
            $"database_path {dbFileName}",
            $"state_path {stateDirName}",
            "temp_path temp",
            "plugin_path ModuleBuilds",
        ]);

        var coversDir = Path.Combine(dir, "templates", "covers");
        Directory.CreateDirectory(coversDir);
        File.WriteAllText(Path.Combine(coversDir, "default.json"), "{\"TemplatePath\":\"bg.png\"}");

        var dbPath = Path.Combine(dir, dbFileName);
        using (var db = new LiteDatabase(dbPath))
        {
            db.GetCollection<Source>("sources").Insert(
            [
                new Source { Id = "src-hdd", TypeId = "HardDiskDrive", Settings = new() { ["_system_name"] = "HDD", ["path"] = "local_hdd" } },
                new Source { Id = "src-yt", TypeId = "Youtube", Settings = new() { ["_system_name"] = "YT" } },
            ]);

            db.GetCollection<Media>("medias").Insert(new Media
            {
                Id = "m-1",
                Title = "Sample",
                Sources =
                [
                    new MediaSourceLink { SourceId = "src-hdd", ExternalId = "1", Status = "Ok" },
                    new MediaSourceLink { SourceId = "src-yt", ExternalId = "2", Status = "Ok" },
                ],
            });

            db.GetCollection<SourceSyncRelation>("source_relations").Insert(new SourceSyncRelation
            {
                FromId = "src-hdd",
                ToId = "src-yt",
            });

            if (withWal)
            {
                db.CheckpointSize = 0;
                db.GetCollection("extra").Insert(new BsonDocument { ["_id"] = 1, ["v"] = "wal" });
                var walPath = WinFormsProcessDetector.GetWalLogPath(dbPath);
                if (File.Exists(walPath))
                {
                    File.Copy(walPath, walPath + ".bak", overwrite: true);
                }
            }
        }

        if (withWal)
        {
            var walPath = WinFormsProcessDetector.GetWalLogPath(dbPath);
            var bakPath = walPath + ".bak";
            if (File.Exists(bakPath))
            {
                File.Move(bakPath, walPath, overwrite: true);
            }
        }

        return dir;
    }
}
