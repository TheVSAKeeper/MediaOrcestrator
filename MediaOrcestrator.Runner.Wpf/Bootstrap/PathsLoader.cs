using System.IO;
using Tomlyn.Model;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

internal static class PathsLoader
{
    public static Paths Load()
    {
        var baseDir = AppContext.BaseDirectory;
        var settingsPath = Path.Combine(baseDir, TomlSettingsFile.PrimaryFileName);
        var root = TomlSettingsFile.LoadOrMigrate(settingsPath);

        return new(Resolve(root, "plugin_path", "ModuleBuilds", baseDir),
            Resolve(root, "database_path", "MyData.db", baseDir),
            Resolve(root, "temp_path", "temp", baseDir),
            Resolve(root, "state_path", "state", baseDir));
    }

    private static string Resolve(TomlTable root, string key, string defaultName, string baseDir)
    {
        var value = TomlSettingsFile.GetString(root, key);

        if (!string.IsNullOrWhiteSpace(value))
        {
            return Path.IsPathRooted(value) ? value : Path.Combine(baseDir, value);
        }

        return Path.Combine(baseDir, defaultName);
    }
}
