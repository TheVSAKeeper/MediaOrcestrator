namespace MediaOrcestrator.Domain.Migration;

public interface IImportSettingsWriter
{
    string? ReadValue(string settingsTomlPath, string key);

    void WriteSettings(
        string targetSettingsTomlPath,
        IReadOnlyDictionary<string, string> values,
        string? preserveFromSettingsTomlPath = null);
}
