using LiteDB;
using MediaOrcestrator.Domain.Migration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Text.Json.Nodes;

namespace MediaOrcestrator.Domain.Tests.Migration;

[TestFixture]
public sealed class WinFormsSnapshotStagerTests
{
    private string _rootDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "mo-stager-tests-" + Guid.NewGuid().ToString("N"));
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
    public void StageSnapshot_проигрывает_catalog_log_db_сохраняет_старый_temp_и_дозаполняет_bson_поля()
    {
        var sourceDir = Path.Combine(_rootDir, "wf-wal");
        Directory.CreateDirectory(sourceDir);

        var oldTempDir = Path.Combine(_rootDir, "old-wf-temp");
        Directory.CreateDirectory(oldTempDir);
        var sentinelFile = Path.Combine(oldTempDir, "sentinel.txt");
        File.WriteAllText(sentinelFile, "keep-me");

        var dbPath = Path.Combine(sourceDir, "catalog.db");
        var walPath = WinFormsProcessDetector.GetWalLogPath(dbPath);
        CreateWalOnlyFixture(dbPath, walPath, oldTempDir);

        Assert.That(CountSourcesWithoutWal(dbPath), Is.EqualTo(1));

        var dbShaBefore = FileIntegrity.ComputeSha256(dbPath);
        var walShaBefore = FileIntegrity.ComputeSha256(walPath);

        var settings = new LegacySettingsSnapshot(
            sourceDir,
            new Dictionary<string, string>(),
            Path.Combine(sourceDir, "ModuleBuilds"),
            dbPath,
            oldTempDir,
            Path.Combine(sourceDir, "state"));

        var stagingDir = Path.Combine(_rootDir, "staging");
        var finalCoversDir = Path.Combine(_rootDir, "wpf-target", "templates", "covers");

        var detector = Substitute.For<IWinFormsProcessDetector>();
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>()).Returns(WinFormsProcessStatus.Free);

        var stager = new WinFormsSnapshotStager(detector);
        var staged = stager.StageSnapshot(settings, stagingDir, finalCoversDir);

        var newWpfTempDir = Path.Combine(_rootDir, "wpf-target", "temp");
        Directory.CreateDirectory(newWpfTempDir);

        using (var stagedDb = new LiteDatabase(staged.DatabasePath))
        {
            var tempManager = new TempManager(newWpfTempDir, stagedDb, NullLogger<TempManager>.Instance);
            tempManager.MigrateOldTempPaths();

            var sources = stagedDb.GetCollection<Source>("sources").FindAll().OrderBy(s => s.Id).ToList();
            var media = stagedDb.GetCollection<Media>("medias").FindById("m-wal");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sources, Has.Count.EqualTo(2));
                Assert.That(sources[0].Id, Is.EqualTo("src-base"));
                Assert.That(sources[1].Id, Is.EqualTo("src-wal-only"));
                Assert.That(sources[0].Settings.ContainsKey("temp_path"), Is.False);
                Assert.That(sources[0].Settings.ContainsKey("_system_temp_path"), Is.False);
                Assert.That(sources[0].Settings.ContainsKey("_system_state_path"), Is.False);
                Assert.That(media.Sources, Has.Count.EqualTo(2));
                Assert.That(media.Sources[0].Title, Is.EqualTo("Parent Title"));
                Assert.That(media.Sources[0].Description, Is.EqualTo("Parent Description"));
                Assert.That(media.Sources[1].Title, Is.EqualTo("Parent Title"));
                Assert.That(media.Sources[1].Description, Is.EqualTo(""));
                Assert.That(File.Exists(sentinelFile), Is.True);
                Assert.That(File.Exists(WinFormsProcessDetector.GetWalLogPath(staged.DatabasePath)), Is.False);
                Assert.That(FileIntegrity.ComputeSha256(dbPath), Is.EqualTo(dbShaBefore));
                Assert.That(FileIntegrity.ComputeSha256(walPath), Is.EqualTo(walShaBefore));
            }
        }
    }

    [Test]
    public void StageSnapshot_переносит_относительные_и_абсолютные_сессии_шаблоны_и_hdd_пути()
    {
        var sourceDir = Path.Combine(_rootDir, "wf-sessions");
        var stateDir = Path.Combine(sourceDir, "state");
        var coversDir = Path.Combine(sourceDir, "templates", "covers");
        Directory.CreateDirectory(stateDir);
        Directory.CreateDirectory(coversDir);

        var relCookie = Path.Combine(sourceDir, "auth", "vk-cookie.json");
        Directory.CreateDirectory(Path.GetDirectoryName(relCookie)!);
        File.WriteAllText(relCookie, "vk-session-data");

        var absTgSession = Path.Combine(_rootDir, "shared-external", "tg.session");
        Directory.CreateDirectory(Path.GetDirectoryName(absTgSession)!);
        File.WriteAllText(absTgSession, "tg-session-bytes");

        var absOAuthDir = Path.Combine(_rootDir, "oauth-external");
        Directory.CreateDirectory(absOAuthDir);
        File.WriteAllText(Path.Combine(absOAuthDir, "Google.Apis.Auth.OAuth2.Responses.TokenResponse-user"), "oauth-json");

        var legacyYtPlaywright = Path.Combine(sourceDir, "yt_playwright.json");
        File.WriteAllText(legacyYtPlaywright, "{ \"cookies\": [] }");

        var customStateDir = Path.Combine(_rootDir, "custom-rutube-state");
        Directory.CreateDirectory(customStateDir);
        File.WriteAllText(Path.Combine(customStateDir, "auth_state"), "rutube-custom");

        var coverImg = Path.Combine(coversDir, "cover1.png");
        File.WriteAllBytes(coverImg, [137, 80, 78, 71]);
        File.WriteAllText(Path.Combine(coversDir, "cover1.json"), $"{{\"TemplatePath\":\"{coverImg.Replace("\\", "\\\\")}\",\"StartNumber\":1}}");

        var dbPath = Path.Combine(sourceDir, "MyData.db");
        using (var db = new LiteDatabase(dbPath))
        {
            db.GetCollection<Source>("sources").Insert(
            [
                new Source
                {
                    Id = "src-vk",
                    TypeId = "VkVideo",
                    Settings = new() { ["auth_state_path"] = "auth\\vk-cookie.json" },
                },
                new Source
                {
                    Id = "src-tg",
                    TypeId = "Telegram",
                    Settings = new() { ["session_path"] = absTgSession },
                },
                new Source
                {
                    Id = "src-yt",
                    TypeId = "Youtube",
                    Settings = new()
                    {
                        ["auth_state_path"] = "yt_playwright.json",
                        ["token_path"] = Path.Combine(absOAuthDir, "token.json"),
                    },
                },
                new Source
                {
                    Id = "src-rt",
                    TypeId = "Rutube",
                    Settings = new() { ["_system_state_path"] = customStateDir },
                },
                new Source
                {
                    Id = "src-hdd",
                    TypeId = "HardDiskDrive",
                    Settings = new() { ["path"] = "media_drive" },
                },
            ]);
        }

        var settings = new LegacySettingsSnapshot(
            sourceDir,
            new Dictionary<string, string>(),
            Path.Combine(sourceDir, "ModuleBuilds"),
            dbPath,
            Path.Combine(sourceDir, "temp"),
            stateDir);

        var stagingDir = Path.Combine(_rootDir, "staging");
        var finalCoversDir = Path.Combine(_rootDir, "wpf-new", "templates", "covers");

        var detector = Substitute.For<IWinFormsProcessDetector>();
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>()).Returns(WinFormsProcessStatus.Free);

        var staged = new WinFormsSnapshotStager(detector).StageSnapshot(settings, stagingDir, finalCoversDir);

        var reboundCoverNode = JsonNode.Parse(File.ReadAllText(Path.Combine(staged.TemplatesCoversDirectory, "cover1.json")));
        var reboundTemplatePath = reboundCoverNode?["TemplatePath"]?.GetValue<string>();

        using var checkDb = new LiteDatabase(staged.DatabasePath);
        var sources = checkDb.GetCollection<Source>("sources").FindAll().ToDictionary(s => s.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(Path.Combine(staged.StateDirectory, "src-vk", "auth_state")), Is.EqualTo("vk-session-data"));
            Assert.That(File.ReadAllText(Path.Combine(staged.StateDirectory, "src-tg", "telegram.session")), Is.EqualTo("tg-session-bytes"));
            Assert.That(File.Exists(Path.Combine(staged.StateDirectory, "src-yt", "oauth", "Google.Apis.Auth.OAuth2.Responses.TokenResponse-user")), Is.True);
            Assert.That(File.Exists(Path.Combine(staged.StateDirectory, "src-yt", "auth_state")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(staged.StateDirectory, "src-rt", "auth_state")), Is.EqualTo("rutube-custom"));
            Assert.That(sources["src-vk"].Settings.ContainsKey("auth_state_path"), Is.False);
            Assert.That(sources["src-tg"].Settings.ContainsKey("session_path"), Is.False);
            Assert.That(sources["src-yt"].Settings.ContainsKey("auth_state_path"), Is.False);
            Assert.That(sources["src-yt"].Settings.ContainsKey("token_path"), Is.False);
            Assert.That(sources["src-rt"].Settings.ContainsKey("_system_state_path"), Is.False);
            Assert.That(sources["src-hdd"].Settings["path"], Is.EqualTo(Path.GetFullPath(Path.Combine(sourceDir, "media_drive"))));
            Assert.That(reboundTemplatePath, Is.EqualTo(Path.Combine(finalCoversDir, "cover1.png")));
            Assert.That(staged.Warnings.Any(w => w.Kind == WinFormsImportWarningKind.LegacyYoutubePlaywrightAuth), Is.True);
            Assert.That(staged.Warnings.Any(w => w.Kind == WinFormsImportWarningKind.ExternalHardDiskPath), Is.True);
        }
    }

    [Test]
    public void StageSnapshot_две_копии_wf_a_и_wf_b_изолируются_и_сохраняют_исходные_sha256()
    {
        var wfA = CreateIsolatedInstance("wf-a", "Session A", "Media A");
        var wfB = CreateIsolatedInstance("wf-b", "Session B", "Media B");

        var shaABefore = FileIntegrity.ComputeSha256(wfA.DatabasePath);
        var shaBBefore = FileIntegrity.ComputeSha256(wfB.DatabasePath);

        var detector = Substitute.For<IWinFormsProcessDetector>();
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>()).Returns(WinFormsProcessStatus.Free);
        var stager = new WinFormsSnapshotStager(detector);

        var stagedA = stager.StageSnapshot(wfA, Path.Combine(_rootDir, "stage-a"), Path.Combine(_rootDir, "wpf-a", "templates", "covers"));
        var stagedB = stager.StageSnapshot(wfB, Path.Combine(_rootDir, "stage-b"), Path.Combine(_rootDir, "wpf-b", "templates", "covers"));

        using var dbA = new LiteDatabase(stagedA.DatabasePath);
        using var dbB = new LiteDatabase(stagedB.DatabasePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(Path.Combine(stagedA.StateDirectory, "src-tg", "telegram.session")), Is.EqualTo("Session A"));
            Assert.That(File.ReadAllText(Path.Combine(stagedB.StateDirectory, "src-tg", "telegram.session")), Is.EqualTo("Session B"));
            Assert.That(dbA.GetCollection<Media>("medias").FindById("m-1").Title, Is.EqualTo("Media A"));
            Assert.That(dbB.GetCollection<Media>("medias").FindById("m-1").Title, Is.EqualTo("Media B"));
            Assert.That(FileIntegrity.ComputeSha256(wfA.DatabasePath), Is.EqualTo(shaABefore));
            Assert.That(FileIntegrity.ComputeSha256(wfB.DatabasePath), Is.EqualTo(shaBBefore));
        }
    }

    [Test]
    public void StageSnapshot_явный_custom_state_path_перезаписывает_одноимённый_файл_из_стандартного_state()
    {
        var dir = Path.Combine(_rootDir, "wf-custom-state-override");
        var defaultStateDir = Path.Combine(dir, "state");
        var customStateDir = Path.Combine(dir, "custom-rutube-state");
        Directory.CreateDirectory(Path.Combine(defaultStateDir, "src-rt"));
        Directory.CreateDirectory(customStateDir);

        File.WriteAllText(Path.Combine(defaultStateDir, "src-rt", "auth_state"), "stale-default-auth");
        File.WriteAllText(Path.Combine(customStateDir, "auth_state"), "fresh-custom-auth");

        var dbPath = Path.Combine(dir, "MyData.db");
        using (var db = new LiteDatabase(dbPath))
        {
            db.GetCollection<Source>("sources").Insert(new Source
            {
                Id = "src-rt",
                TypeId = "Rutube",
                Settings = new()
                {
                    ["_system_state_path"] = customStateDir,
                },
            });
        }

        var settings = new LegacySettingsSnapshot(
            dir,
            new Dictionary<string, string>(),
            Path.Combine(dir, "ModuleBuilds"),
            dbPath,
            Path.Combine(dir, "temp"),
            defaultStateDir);

        var detector = Substitute.For<IWinFormsProcessDetector>();
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>()).Returns(WinFormsProcessStatus.Free);
        var stager = new WinFormsSnapshotStager(detector);

        var staged = stager.StageSnapshot(settings, Path.Combine(_rootDir, "stage-override"), Path.Combine(_rootDir, "wpf-override", "templates", "covers"));

        Assert.That(File.ReadAllText(Path.Combine(staged.StateDirectory, "src-rt", "auth_state")), Is.EqualTo("fresh-custom-auth"));
    }

    private LegacySettingsSnapshot CreateIsolatedInstance(string name, string sessionContent, string mediaTitle)
    {
        var dir = Path.Combine(_rootDir, name);
        var stateDir = Path.Combine(dir, "state");
        var tgDir = Path.Combine(stateDir, "src-tg");
        Directory.CreateDirectory(tgDir);
        File.WriteAllText(Path.Combine(tgDir, "telegram.session"), sessionContent);

        var dbPath = Path.Combine(dir, "MyData.db");
        using (var db = new LiteDatabase(dbPath))
        {
            db.GetCollection<Source>("sources").Insert(new Source { Id = "src-tg", TypeId = "Telegram" });
            db.GetCollection<Media>("medias").Insert(new Media { Id = "m-1", Title = mediaTitle });
        }

        return new(
            dir,
            new Dictionary<string, string>(),
            Path.Combine(dir, "ModuleBuilds"),
            dbPath,
            Path.Combine(dir, "temp"),
            stateDir);
    }

    private static void CreateWalOnlyFixture(string targetDbPath, string targetWalPath, string oldTempDir)
    {
        var liveDir = Path.Combine(Path.GetDirectoryName(targetDbPath)!, "live-gen");
        Directory.CreateDirectory(liveDir);
        var liveDb = Path.Combine(liveDir, Path.GetFileName(targetDbPath));
        var liveWal = WinFormsProcessDetector.GetWalLogPath(liveDb);

        try
        {
            using (var db = new LiteDatabase(new ConnectionString { Filename = liveDb, Connection = ConnectionType.Direct }))
            {
                db.CheckpointSize = 0;

                db.GetCollection<Source>("sources").Insert(new Source
                {
                    Id = "src-base",
                    TypeId = "Youtube",
                    Settings = new()
                    {
                        ["temp_path"] = oldTempDir,
                        ["_system_temp_path"] = oldTempDir,
                        ["_system_state_path"] = Path.Combine(Path.GetDirectoryName(targetDbPath)!, "state", "src-base"),
                    },
                });

                db.Checkpoint();

                db.GetCollection<Source>("sources").Insert(new Source
                {
                    Id = "src-wal-only",
                    TypeId = "Rutube",
                });

                db.GetCollection("medias").Insert(new BsonDocument
                {
                    ["_id"] = "m-wal",
                    ["Title"] = "Parent Title",
                    ["Description"] = "Parent Description",
                    ["Sources"] = new BsonArray
                    {
                        new BsonDocument
                        {
                            ["SourceId"] = "src-base",
                            ["ExternalId"] = "e-1",
                        },
                        new BsonDocument
                        {
                            ["SourceId"] = "src-wal-only",
                            ["ExternalId"] = "e-2",
                            ["Description"] = "",
                        },
                    },
                });

                CopyShared(liveDb, targetDbPath);
                CopyShared(liveWal, targetWalPath);
            }
        }
        finally
        {
            Directory.Delete(liveDir, true);
        }
    }

    private static void CopyShared(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static int CountSourcesWithoutWal(string dbPath)
    {
        var tempDb = dbPath + ".check-only";
        try
        {
            File.Copy(dbPath, tempDb, overwrite: true);
            using var db = new LiteDatabase(tempDb);
            return db.GetCollection<Source>("sources").Count();
        }
        finally
        {
            if (File.Exists(tempDb))
            {
                File.Delete(tempDb);
            }
        }
    }
}
