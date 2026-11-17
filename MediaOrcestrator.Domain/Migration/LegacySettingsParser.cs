namespace MediaOrcestrator.Domain.Migration;

public static class LegacySettingsParser
{
    public const string PluginPathKey = "plugin_path";
    public const string DatabasePathKey = "database_path";
    public const string TempPathKey = "temp_path";
    public const string StatePathKey = "state_path";

    public static IReadOnlyDictionary<string, string> ParseFile(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        return ParseLines(File.ReadAllLines(settingsFilePath));
    }

    public static IReadOnlyDictionary<string, string> ParseLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                values[parts[0].Trim()] = UnquoteValue(parts[1].Trim());
            }
        }

        return values;
    }

    public static LegacySettingsSnapshot ResolveSnapshot(string sourceFolder, IReadOnlyDictionary<string, string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentNullException.ThrowIfNull(values);

        var canonicalSource = WinFormsInstallationInspector.CanonicalizePath(sourceFolder);

        return new(
            canonicalSource,
            values,
            ResolvePath(canonicalSource, values.GetValueOrDefault(PluginPathKey), "ModuleBuilds"),
            ResolvePath(canonicalSource, values.GetValueOrDefault(DatabasePathKey), "MyData.db"),
            ResolvePath(canonicalSource, values.GetValueOrDefault(TempPathKey), "temp"),
            ResolvePath(canonicalSource, values.GetValueOrDefault(StatePathKey), "state"));
    }

    internal static string ResolvePath(string baseFolder, string? rawValue, string defaultRelative)
    {
        var unquoted = string.IsNullOrWhiteSpace(rawValue) ? null : UnquoteValue(rawValue.Trim());
        var effective = string.IsNullOrWhiteSpace(unquoted) ? defaultRelative : unquoted;

        if (Path.IsPathFullyQualified(effective))
        {
            return Path.GetFullPath(effective);
        }

        if (effective.Length >= 2 && effective[1] == ':' && char.IsAsciiLetter(effective[0]))
        {
            throw new FormatException($"Путь «{effective}» указывает диск без корневого каталога.");
        }

        var relative = effective.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative))
        {
            relative = defaultRelative;
        }

        return Path.GetFullPath(Path.Combine(baseFolder, relative));
    }

    private static string UnquoteValue(string value)
    {
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1].Trim()
            : value;
    }
}
