using MediaOrcestrator.Domain;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

internal static class FirstRunImportDetector
{
    public const string PromptDismissedKey = "wpf.import.prompt_dismissed";
    public const string CompletedKey = "wpf.import.completed";

    private static readonly string[] CustomPathSettingKeys =
    [
        "plugin_path",
        "database_path",
        "temp_path",
        "state_path",
    ];

    public static bool ShouldOpenOnStartup(
        bool isPrimaryInstance,
        bool isGallery,
        bool isApplyImport,
        bool hadLegacySettingsTxt,
        ISettingsStore settings,
        Orcestrator orcestrator,
        string? settingsTomlPath = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(orcestrator);

        return ShouldOpenOnStartup(
            isPrimaryInstance,
            isGallery,
            isApplyImport,
            hadLegacySettingsTxt,
            settings,
            orcestrator.GetSources().Count,
            orcestrator.GetMedias().Count,
            orcestrator.GetRelations().Count,
            HasConfiguredCustomPaths(settingsTomlPath));
    }

    public static bool ShouldOpenOnStartup(
        bool isPrimaryInstance,
        bool isGallery,
        bool isApplyImport,
        bool hadLegacySettingsTxt,
        ISettingsStore settings,
        int sourcesCount,
        int mediasCount,
        int relationsCount = 0,
        bool hasCustomPaths = false)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!isPrimaryInstance || isGallery || isApplyImport || hadLegacySettingsTxt || hasCustomPaths)
        {
            return false;
        }

        if (settings.GetBool(PromptDismissedKey) || settings.GetBool(CompletedKey))
        {
            return false;
        }

        return sourcesCount == 0 && mediasCount == 0 && relationsCount == 0;
    }

    public static bool HasConfiguredCustomPaths(string? settingsTomlPath)
    {
        if (string.IsNullOrWhiteSpace(settingsTomlPath) || !File.Exists(settingsTomlPath))
        {
            return false;
        }

        try
        {
            var table = TomlSettingsFile.Read(settingsTomlPath);
            return CustomPathSettingKeys.Any(key => !string.IsNullOrWhiteSpace(TomlSettingsFile.GetString(table, key)));
        }
        catch
        {
            return false;
        }
    }
}
