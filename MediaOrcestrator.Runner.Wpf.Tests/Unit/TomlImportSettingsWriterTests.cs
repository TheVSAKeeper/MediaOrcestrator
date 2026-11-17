using KeepShell.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class TomlImportSettingsWriterTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MediaOrcestrator.TomlWriterTests", Guid.NewGuid().ToString("N"));
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
    public void WriteSettings_БезИсходногоФайла_ЗаписываетКлючиИСовместимСSettingsStore()
    {
        var writer = new TomlImportSettingsWriter();
        var tomlPath = Path.Combine(_tempDir, "nested", "settings.toml");

        writer.WriteSettings(tomlPath, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["plugin_path"] = "ModuleBuilds",
            ["database_path"] = "MyData.db",
            ["temp_path"] = "temp",
            ["state_path"] = "state",
            ["update_repo"] = "TheVSAKeeper/MediaOrcestrator",
            ["wpf.sync.auto_enabled"] = "false",
            ["wpf.mcp.enabled"] = "false",
            ["wpf.import.completed"] = "true",
        });

        using var store = new SettingsStore(tomlPath);
        ISettingsStore settingsStore = store;
        var parsed = TomlSettingsFile.Read(tomlPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(tomlPath), Is.True);
            Assert.That(writer.ReadValue(tomlPath, "database_path"), Is.EqualTo("MyData.db"));
            Assert.That(writer.ReadValue(tomlPath, "wpf.import.completed"), Is.EqualTo("true"));
            Assert.That(TomlSettingsFile.GetString(parsed, "wpf.sync.auto_enabled"), Is.EqualTo("false"));
            Assert.That(settingsStore.GetStringValue("plugin_path"), Is.EqualTo("ModuleBuilds"));
            Assert.That(settingsStore.GetBool("wpf.sync.auto_enabled", fallback: true), Is.False);
            Assert.That(settingsStore.GetBool("wpf.import.completed"), Is.True);
        }
    }

    [Test]
    public void WriteSettings_ССохранениемПредыдущегоФайла_СохраняетПользовательскиеКлючи()
    {
        var writer = new TomlImportSettingsWriter();
        var existingPath = Path.Combine(_tempDir, "current.toml");
        var targetPath = Path.Combine(_tempDir, "staged.toml");

        writer.WriteSettings(existingPath, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["database_path"] = @"D:\Old\MyData.db",
            ["wpf.theme"] = "tarkov",
            ["wpf.shell.font_scale"] = "1.15",
            ["wpf.mcp.enabled"] = "true",
            ["wpf.mcp.port"] = "7655",
            ["wpf.mcp.token"] = "secret-token",
        });

        writer.WriteSettings(
            targetPath,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["database_path"] = "MyData.db",
                ["state_path"] = "state",
                ["wpf.import.completed"] = "true",
            },
            preserveFromSettingsTomlPath: existingPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.ReadValue(targetPath, "database_path"), Is.EqualTo("MyData.db"));
            Assert.That(writer.ReadValue(targetPath, "state_path"), Is.EqualTo("state"));
            Assert.That(writer.ReadValue(targetPath, "wpf.import.completed"), Is.EqualTo("true"));
            Assert.That(writer.ReadValue(targetPath, "wpf.theme"), Is.EqualTo("tarkov"));
            Assert.That(writer.ReadValue(targetPath, "wpf.shell.font_scale"), Is.EqualTo("1.15"));
            Assert.That(writer.ReadValue(targetPath, "wpf.mcp.enabled"), Is.EqualTo("true"));
            Assert.That(writer.ReadValue(targetPath, "wpf.mcp.token"), Is.EqualTo("secret-token"));
        }
    }

    [Test]
    public void ReadValue_КогдаФайлИлиКлючОтсутствует_ВозвращаетNull()
    {
        var writer = new TomlImportSettingsWriter();
        var missingPath = Path.Combine(_tempDir, "missing.toml");

        Assert.That(writer.ReadValue(missingPath, "database_path"), Is.Null);

        writer.WriteSettings(missingPath, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["plugin_path"] = "ModuleBuilds",
        });

        Assert.That(writer.ReadValue(missingPath, "unknown.key"), Is.Null);
    }
}
