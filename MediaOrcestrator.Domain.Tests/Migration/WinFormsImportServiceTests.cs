using LiteDB;
using MediaOrcestrator.Domain.Migration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using STJ = System.Text.Json;

namespace MediaOrcestrator.Domain.Tests.Migration;

[TestFixture]
public sealed class WinFormsImportServiceTests
{
    private string _rootDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "mo-import-svc-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task CreateSeparateInstanceAsync_создаёт_изолированный_экземпляр_и_реальный_ярлык_lnk()
    {
        var wpfAppBase = CreateSyntheticWpfAppBase("wpf-app");
        var wfSource = CreateSyntheticWinForms("wf-src", "Channel A");
        var targetDir = Path.Combine(_rootDir, "wpf-isolated");
        var shortcutDir = Path.Combine(_rootDir, "shortcuts");

        var settingsWriter = new FakeImportSettingsWriter();
        var service = CreateService(settingsWriter, new WindowsShortcutCreator());

        var result = await service.CreateSeparateInstanceAsync(new(
            wfSource,
            targetDir,
            CreateShortcut: true,
            ShortcutDirectory: shortcutDir,
            ShortcutName: "MediaOrcestrator – Channel A",
            EnableAutoSync: false,
            CurrentAppBaseDirectory: wpfAppBase));

        var writtenSettings = settingsWriter.ReadAll(result.SettingsPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(targetDir, "MediaOrcestrator.Runner.Wpf.exe")), Is.True);
            Assert.That(File.Exists(Path.Combine(targetDir, "ModuleBuilds", "TestPlugin.dll")), Is.True);
            Assert.That(File.Exists(Path.Combine(targetDir, "tools", "ffmpeg.exe")), Is.True);
            Assert.That(File.Exists(Path.Combine(targetDir, "MyData.db")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(targetDir, "temp")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(targetDir, "state", "src-1", "auth_state")), Is.EqualTo("wf-auth"));
            Assert.That(File.Exists(Path.Combine(targetDir, "backups", "old-wpf.db")), Is.False);
            Assert.That(File.Exists(Path.Combine(targetDir, "logs", "wpf.log")), Is.False);
            Assert.That(File.Exists(Path.Combine(targetDir, "updater.log")), Is.False);
            Assert.That(File.Exists(Path.Combine(targetDir, "state", "wpf-only", "stale.txt")), Is.False);
            Assert.That(writtenSettings["plugin_path"], Is.EqualTo("ModuleBuilds"));
            Assert.That(writtenSettings["database_path"], Is.EqualTo("MyData.db"));
            Assert.That(writtenSettings["wpf.mcp.enabled"], Is.EqualTo("false"));
            Assert.That(writtenSettings["wpf.sync.auto_enabled"], Is.EqualTo("false"));
            Assert.That(writtenSettings["wpf.import.completed"], Is.EqualTo("true"));
            Assert.That(result.ShortcutPath, Is.Not.Null);
            Assert.That(File.Exists(result.ShortcutPath!), Is.True);
            Assert.That(new FileInfo(result.ShortcutPath!).Length, Is.GreaterThan(0));
        }
    }

    [Test]
    public async Task CreateSeparateInstanceAsync_отклоняет_непустую_целевую_папку_при_повторном_запуске()
    {
        var wpfAppBase = CreateSyntheticWpfAppBase("wpf-app");
        var wfSource = CreateSyntheticWinForms("wf-src", "Channel A");
        var targetDir = Path.Combine(_rootDir, "wpf-existing");
        Directory.CreateDirectory(targetDir);
        var markerFile = Path.Combine(targetDir, "keep.txt");
        File.WriteAllText(markerFile, "do-not-overwrite");

        var service = CreateService(new FakeImportSettingsWriter(), Substitute.For<IShortcutCreator>());

        var ex = await CaptureImportExceptionAsync(() =>
            service.CreateSeparateInstanceAsync(new(
                wfSource,
                targetDir,
                CreateShortcut: false,
                CurrentAppBaseDirectory: wpfAppBase)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.Code, Is.EqualTo(WinFormsImportErrorCode.TargetNotEmpty));
            Assert.That(File.ReadAllText(markerFile), Is.EqualTo("do-not-overwrite"));
        }
    }

    [Test]
    public async Task CreateSeparateInstanceAsync_очищает_созданную_папку_при_ошибке_или_отмене()
    {
        var wpfAppBase = CreateSyntheticWpfAppBase("wpf-app");
        var wfSource = CreateSyntheticWinForms("wf-src", "Channel A");
        var targetDir = Path.Combine(_rootDir, "wpf-rollback");

        var failingShortcut = Substitute.For<IShortcutCreator>();
        failingShortcut
            .When(x => x.CreateShortcut(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<int>()))
            .Do(_ => throw new IOException("Shortcut error"));

        var service = CreateService(new FakeImportSettingsWriter(), failingShortcut);

        var failed = await CaptureExceptionAsync(() =>
            service.CreateSeparateInstanceAsync(new(
                wfSource,
                targetDir,
                CreateShortcut: true,
                CurrentAppBaseDirectory: wpfAppBase)));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cancelled = await CaptureExceptionAsync(() =>
            service.CreateSeparateInstanceAsync(
                new(wfSource, targetDir, CreateShortcut: false, CurrentAppBaseDirectory: wpfAppBase),
                cancellationToken: cts.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed, Is.TypeOf<IOException>());
            Assert.That(cancelled, Is.InstanceOf<OperationCanceledException>());
            Assert.That(Directory.Exists(targetDir), Is.False);
        }
    }

    [Test]
    public async Task StageReplaceCurrentAsync_и_ApplyIfPending_делают_бэкап_и_чистую_замену_без_смешивания()
    {
        var wpfAppBase = CreateSyntheticWpfAppBase("wpf-replace");
        var wfSource = CreateSyntheticWinForms("wf-src", "Imported Channel");

        var staleCover = Path.Combine(wpfAppBase, "templates", "covers", "stale-wpf.json");
        Directory.CreateDirectory(Path.GetDirectoryName(staleCover)!);
        File.WriteAllText(staleCover, "{\"TemplatePath\":\"old.png\"}");

        var settingsWriter = new FakeImportSettingsWriter();
        settingsWriter.WriteSettings(Path.Combine(wpfAppBase, "settings.toml"), new Dictionary<string, string>
        {
            ["theme"] = "tarkov-dark",
            ["wpf.mcp.port"] = "7699",
            ["wpf.shell.font_scale"] = "1.15",
        });

        var service = CreateService(settingsWriter, Substitute.For<IShortcutCreator>());

        var stagedResult = await service.StageReplaceCurrentAsync(new(
            wfSource,
            EnableAutoSync: true,
            TargetAppBaseDirectory: wpfAppBase));

        var outcome = WinFormsPendingImportApplier.ApplyIfPending(wpfAppBase, NullLogger.Instance);
        var consumed = WinFormsPendingImportApplier.TryReadAndConsumeResult(wpfAppBase, NullLogger.Instance);
        var afterSettings = settingsWriter.ReadAll(Path.Combine(wpfAppBase, "settings.toml"));

        using var db = new LiteDatabase(Path.Combine(wpfAppBase, "MyData.db"));
        var medias = db.GetCollection<Media>("medias").FindAll().ToList();
        var backupLog = STJ.JsonSerializer.Deserialize<List<BackupLogEntry>>(
            File.ReadAllText(Path.Combine(wpfAppBase, "backups", "backup-log.json")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stagedResult.RestartArguments, Does.StartWith("--apply-import="));
            Assert.That(outcome, Is.Not.Null);
            Assert.That(outcome!.Status, Is.EqualTo(WinFormsPendingImportOutcomeStatus.Applied));
            Assert.That(Directory.Exists(outcome.BackupDirectory), Is.True);
            Assert.That(File.Exists(Path.Combine(outcome.BackupDirectory!, "settings.toml")), Is.True);
            Assert.That(File.Exists(Path.Combine(outcome.BackupDirectory!, "MyData.db")), Is.True);
            Assert.That(File.Exists(Path.Combine(outcome.BackupDirectory!, "state", "wpf-only", "stale.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(outcome.BackupDirectory!, "templates", "covers", "stale-wpf.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(wpfAppBase, "state", "wpf-only", "stale.txt")), Is.False);
            Assert.That(File.Exists(staleCover), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(wpfAppBase, "state", "src-1", "auth_state")), Is.EqualTo("wf-auth"));
            Assert.That(medias.Select(m => m.Title), Is.EquivalentTo(new[] { "Imported Channel" }));
            Assert.That(afterSettings["theme"], Is.EqualTo("tarkov-dark"));
            Assert.That(afterSettings["wpf.mcp.port"], Is.EqualTo("7699"));
            Assert.That(afterSettings["wpf.sync.auto_enabled"], Is.EqualTo("true"));
            Assert.That(afterSettings["wpf.import.completed"], Is.EqualTo("true"));
            Assert.That(backupLog, Is.Not.Null.And.Count.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(wpfAppBase, WinFormsImportService.PendingManifestFileName)), Is.False);
            Assert.That(Directory.Exists(stagedResult.Manifest.StagingDirectory), Is.False);
            Assert.That(consumed, Is.Not.Null);
            Assert.That(consumed!.Status, Is.EqualTo(WinFormsPendingImportOutcomeStatus.Applied));
            Assert.That(File.Exists(Path.Combine(wpfAppBase, WinFormsImportService.ResultFileName)), Is.False);
        }
    }

    [Test]
    public async Task ApplyIfPending_при_сбое_замены_выполняет_полный_автооткат_из_резервной_копии()
    {
        var wpfAppBase = CreateSyntheticWpfAppBase("wpf-fault-rollback");
        var wfSource = CreateSyntheticWinForms("wf-src", "Imported Channel");

        var settingsWriter = new FakeImportSettingsWriter();
        settingsWriter.WriteSettings(Path.Combine(wpfAppBase, "settings.toml"), new Dictionary<string, string>
        {
            ["theme"] = "original-theme",
        });

        var service = CreateService(settingsWriter, Substitute.For<IShortcutCreator>());
        await service.StageReplaceCurrentAsync(new(wfSource, TargetAppBaseDirectory: wpfAppBase));

        var outcome = WinFormsPendingImportApplier.ApplyIfPending(
            wpfAppBase,
            NullLogger.Instance,
            faultHook: () => throw new IOException("Simulated disk error"));

        var restoredSettings = settingsWriter.ReadAll(Path.Combine(wpfAppBase, "settings.toml"));

        using var db = new LiteDatabase(Path.Combine(wpfAppBase, "MyData.db"));
        var medias = db.GetCollection<Media>("medias").FindAll().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.Not.Null);
            Assert.That(outcome!.Status, Is.EqualTo(WinFormsPendingImportOutcomeStatus.RolledBack));
            Assert.That(Directory.Exists(outcome.BackupDirectory), Is.True);
            Assert.That(restoredSettings["theme"], Is.EqualTo("original-theme"));
            Assert.That(File.Exists(Path.Combine(wpfAppBase, "state", "wpf-only", "stale.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(wpfAppBase, "state", "src-1", "auth_state")), Is.False);
            Assert.That(medias.Select(m => m.Title), Is.EquivalentTo(new[] { "Original WPF Media" }));
            Assert.That(File.Exists(Path.Combine(wpfAppBase, WinFormsImportService.PendingManifestFileName)), Is.False);
        }
    }

    [Test]
    public async Task ApplyIfPending_сохраняет_restore_pending_маркер_при_откате_и_не_перезаписывает_первый_бэкап_при_повторном_запуске()
    {
        var wpfAppBase = CreateSyntheticWpfAppBase("wpf-crash-resume");
        var wfSource = CreateSyntheticWinForms("wf-src", "Imported Channel");
        var markerPath = Path.Combine(wpfAppBase, "MyData.restore-pending");
        File.WriteAllText(markerPath, "backup.db");

        var settingsWriter = new FakeImportSettingsWriter();
        var service = CreateService(settingsWriter, Substitute.For<IShortcutCreator>());
        var staged = await service.StageReplaceCurrentAsync(new(wfSource, TargetAppBaseDirectory: wpfAppBase));

        var rollbackOutcome = WinFormsPendingImportApplier.ApplyIfPending(
            wpfAppBase,
            NullLogger.Instance,
            faultHook: () => throw new IOException("Transient fault"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rollbackOutcome!.Status, Is.EqualTo(WinFormsPendingImportOutcomeStatus.RolledBack));
            Assert.That(File.Exists(markerPath), Is.True);
            Assert.That(File.ReadAllText(markerPath), Is.EqualTo("backup.db"));
        }

        // Повторно ставим манифест с уже записанным pending-apply-state.json и имитируем восстановление после падения процесса.
        var stagedAgain = await service.StageReplaceCurrentAsync(new(wfSource, TargetAppBaseDirectory: wpfAppBase));
        var preCrashBackupDir = Path.Combine(stagedAgain.Manifest.BackupsDirectory, "pre-import_original");
        Directory.CreateDirectory(preCrashBackupDir);
        File.Copy(Path.Combine(wpfAppBase, "MyData.db"), Path.Combine(preCrashBackupDir, "MyData.db"));
        File.Copy(markerPath, Path.Combine(preCrashBackupDir, "MyData.restore-pending"));
        Directory.CreateDirectory(Path.Combine(preCrashBackupDir, "state", "wpf-only"));
        File.WriteAllText(Path.Combine(preCrashBackupDir, "state", "wpf-only", "stale.txt"), "original-stale");

        File.WriteAllText(
            Path.Combine(stagedAgain.Manifest.StagingDirectory, "pending-apply-state.json"),
            STJ.JsonSerializer.Serialize(new { OperationId = stagedAgain.Manifest.OperationId, BackupDirectory = preCrashBackupDir }));

        // Портим текущий MyData.db как после обрыва в середине замены: при повторе/откате должен использоваться исходный бэкап, а не новый снимок испорченной базы.
        File.Delete(markerPath);
        File.WriteAllText(Path.Combine(wpfAppBase, "state", "wpf-only", "stale.txt"), "corrupted-halfway");

        var resumedOutcome = WinFormsPendingImportApplier.ApplyIfPending(
            wpfAppBase,
            NullLogger.Instance,
            faultHook: () => throw new IOException("Second fault"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resumedOutcome!.Status, Is.EqualTo(WinFormsPendingImportOutcomeStatus.RolledBack));
            Assert.That(resumedOutcome.BackupDirectory, Is.EqualTo(preCrashBackupDir));
            Assert.That(File.Exists(markerPath), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(wpfAppBase, "state", "wpf-only", "stale.txt")), Is.EqualTo("original-stale"));
        }
    }

    [Test]
    public async Task ApplyIfPending_при_блокировке_старого_файла_возвращает_RolledBack_без_смеси_или_RollbackFailed()
    {
        var wpfAppBase = CreateSyntheticWpfAppBase("wpf-locked-target");
        var wfSource = CreateSyntheticWinForms("wf-src", "Imported Channel");

        var service = CreateService(new FakeImportSettingsWriter(), Substitute.For<IShortcutCreator>());
        await service.StageReplaceCurrentAsync(new(wfSource, TargetAppBaseDirectory: wpfAppBase));

        var lockedFile = Path.Combine(wpfAppBase, "state", "wpf-only", "stale.txt");
        using var lockHandle = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        var outcome = WinFormsPendingImportApplier.ApplyIfPending(wpfAppBase, NullLogger.Instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.Not.Null);
            Assert.That(outcome!.Status, Is.EqualTo(WinFormsPendingImportOutcomeStatus.RollbackFailed));
            Assert.That(File.Exists(Path.Combine(wpfAppBase, WinFormsImportService.PendingManifestFileName)), Is.True);
            Assert.That(Directory.Exists(outcome.BackupDirectory), Is.True);
        }
    }

    [Test]
    public async Task StageReplaceCurrentAsync_отклоняет_внешнюю_базу_WinForms_совпадающую_с_целевой_WPF()
    {
        var wpfAppBase = CreateSyntheticWpfAppBase("wpf-overlap-db");
        var wfSource = Path.Combine(_rootDir, "wf-external-db");
        Directory.CreateDirectory(wfSource);
        Directory.CreateDirectory(Path.Combine(wfSource, "state"));
        File.WriteAllText(Path.Combine(wfSource, "MediaOrcestrator.Runner.dll"), "wf-dll");

        var sharedTargetDb = Path.Combine(wpfAppBase, "MyData.db");
        File.WriteAllLines(Path.Combine(wfSource, "settings.txt"),
        [
            $"database_path \"{sharedTargetDb}\"",
            "state_path state",
        ]);

        var settingsWriter = new FakeImportSettingsWriter();
        settingsWriter.WriteSettings(Path.Combine(wpfAppBase, "settings.toml"), new Dictionary<string, string>
        {
            ["database_path"] = "other.db",
        });

        var service = CreateService(settingsWriter, Substitute.For<IShortcutCreator>());
        var ex = await CaptureImportExceptionAsync(() => service.StageReplaceCurrentAsync(new(
            wfSource,
            TargetAppBaseDirectory: wpfAppBase,
            CurrentDatabasePath: Path.Combine(wpfAppBase, "other.db"),
            CurrentStatePath: Path.Combine(wpfAppBase, "other_state"))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.Code, Is.EqualTo(WinFormsImportErrorCode.TargetOverlap));
        }
    }

    private WinFormsImportService CreateService(IImportSettingsWriter writer, IShortcutCreator shortcutCreator)
    {
        var detector = Substitute.For<IWinFormsProcessDetector>();
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>()).Returns(WinFormsProcessStatus.Free);
        var inspector = new WinFormsInstallationInspector(detector, _ => new Version(0, 0, 5, 9));
        var stager = new WinFormsSnapshotStager(detector);
        return new(inspector, stager, writer, shortcutCreator, NullLogger<WinFormsImportService>.Instance);
    }

    private string CreateSyntheticWpfAppBase(string name)
    {
        var dir = Path.Combine(_rootDir, name);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "ModuleBuilds"));
        Directory.CreateDirectory(Path.Combine(dir, "tools"));
        Directory.CreateDirectory(Path.Combine(dir, "state", "wpf-only"));
        Directory.CreateDirectory(Path.Combine(dir, "backups"));
        Directory.CreateDirectory(Path.Combine(dir, "logs"));

        File.WriteAllText(Path.Combine(dir, "MediaOrcestrator.Runner.Wpf.exe"), "wpf-exe");
        File.WriteAllText(Path.Combine(dir, "MediaOrcestrator.Runner.Wpf.dll"), "wpf-dll");
        File.WriteAllText(Path.Combine(dir, "ModuleBuilds", "TestPlugin.dll"), "plugin");
        File.WriteAllText(Path.Combine(dir, "tools", "ffmpeg.exe"), "ffmpeg");
        File.WriteAllText(Path.Combine(dir, "state", "wpf-only", "stale.txt"), "stale");
        File.WriteAllText(Path.Combine(dir, "backups", "old-wpf.db"), "old-backup");
        File.WriteAllText(Path.Combine(dir, "logs", "wpf.log"), "log");
        File.WriteAllText(Path.Combine(dir, "updater.log"), "updater");

        using var db = new LiteDatabase(Path.Combine(dir, "MyData.db"));
        db.GetCollection<Media>("medias").Insert(new Media { Id = "wpf-1", Title = "Original WPF Media" });

        return dir;
    }

    private string CreateSyntheticWinForms(string name, string mediaTitle)
    {
        var dir = Path.Combine(_rootDir, name);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "state", "src-1"));

        File.WriteAllText(Path.Combine(dir, "MediaOrcestrator.Runner.dll"), "wf-dll");
        File.WriteAllText(Path.Combine(dir, "MediaOrcestrator.Runner.exe"), "wf-exe");
        File.WriteAllText(Path.Combine(dir, "state", "src-1", "auth_state"), "wf-auth");
        File.WriteAllLines(Path.Combine(dir, "settings.txt"),
        [
            "database_path MyData.db",
            "state_path state",
            "temp_path temp",
            "plugin_path ModuleBuilds",
        ]);

        using var db = new LiteDatabase(Path.Combine(dir, "MyData.db"));
        db.GetCollection<Source>("sources").Insert(new Source { Id = "src-1", TypeId = "Rutube" });
        db.GetCollection<Media>("medias").Insert(new Media { Id = "wf-1", Title = mediaTitle });

        return dir;
    }

    private static async Task<WinFormsImportException?> CaptureImportExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (WinFormsImportException ex)
        {
            return ex;
        }
    }

    private static async Task<Exception?> CaptureExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class FakeImportSettingsWriter : IImportSettingsWriter
    {
        public string? ReadValue(string settingsTomlPath, string key)
        {
            var dict = ReadAll(settingsTomlPath);
            return dict.GetValueOrDefault(key);
        }

        public void WriteSettings(
            string targetSettingsTomlPath,
            IReadOnlyDictionary<string, string> values,
            string? preserveFromSettingsTomlPath = null)
        {
            var merged = !string.IsNullOrWhiteSpace(preserveFromSettingsTomlPath) && File.Exists(preserveFromSettingsTomlPath)
                ? new Dictionary<string, string>(ReadAll(preserveFromSettingsTomlPath), StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var kv in values)
            {
                merged[kv.Key] = kv.Value;
            }

            var dir = Path.GetDirectoryName(Path.GetFullPath(targetSettingsTomlPath));
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(targetSettingsTomlPath, STJ.JsonSerializer.Serialize(merged));
        }

        public Dictionary<string, string> ReadAll(string settingsTomlPath)
        {
            if (!File.Exists(settingsTomlPath))
            {
                return new(StringComparer.Ordinal);
            }

            return STJ.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(settingsTomlPath))
                ?? new(StringComparer.Ordinal);
        }
    }
}
