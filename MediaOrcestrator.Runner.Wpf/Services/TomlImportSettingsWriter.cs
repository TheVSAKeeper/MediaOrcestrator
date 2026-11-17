using MediaOrcestrator.Domain.Migration;
using System.IO;
using Tomlyn.Model;

namespace MediaOrcestrator.Runner.Wpf.Services;

public sealed class TomlImportSettingsWriter : IImportSettingsWriter
{
    public string? ReadValue(string settingsTomlPath, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsTomlPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!File.Exists(settingsTomlPath))
        {
            return null;
        }

        var table = TomlSettingsFile.Read(settingsTomlPath);
        return TomlSettingsFile.GetString(table, key);
    }

    public void WriteSettings(
        string targetSettingsTomlPath,
        IReadOnlyDictionary<string, string> values,
        string? preserveFromSettingsTomlPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSettingsTomlPath);
        ArgumentNullException.ThrowIfNull(values);

        var root = !string.IsNullOrWhiteSpace(preserveFromSettingsTomlPath) && File.Exists(preserveFromSettingsTomlPath)
            ? TomlSettingsFile.Read(preserveFromSettingsTomlPath)
            : new TomlTable();

        foreach (var (key, value) in values)
        {
            TomlSettingsFile.SetString(root, key, value);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(targetSettingsTomlPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        TomlSettingsFile.Save(targetSettingsTomlPath, root);
    }
}
