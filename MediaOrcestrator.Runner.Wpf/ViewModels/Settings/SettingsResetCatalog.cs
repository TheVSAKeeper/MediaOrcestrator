using CommunityToolkit.Mvvm.ComponentModel;
using KeepShell.Mcp;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using System.IO;
using System.Text;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Settings;

public static class SettingsSectionKeys
{
    public const string Paths = "paths";
    public const string Appearance = "appearance";
    public const string Notifications = "notifications";
    public const string Sync = "sync";
    public const string Updates = "updates";
    public const string Mcp = "mcp";
}

public sealed class SettingsResetField(string key, string caption, string factoryLabel, Func<bool> isFactory, Action apply)
{
    public string Key { get; } = key;

    public string Caption { get; } = caption;

    public string FactoryLabel { get; } = factoryLabel;

    public bool IsFactory => isFactory();

    public void Apply()
    {
        if (!IsFactory)
        {
            apply();
        }
    }
}

public sealed class SettingsResetPlan : ObservableObject
{
    private readonly string? _untouched;

    public SettingsResetPlan(string sectionKey, string? untouched, params SettingsResetField[] fields)
    {
        SectionKey = sectionKey;
        Fields = fields;
        _untouched = untouched;
    }

    public string SectionKey { get; }

    public IReadOnlyList<SettingsResetField> Fields { get; }

    public bool CanReset => Fields.Any(entry => !entry.IsFactory);

    public string Hint => BuildHint();

    public void Apply()
    {
        foreach (var entry in Fields)
        {
            entry.Apply();
        }

        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(Hint));
    }

    private string BuildHint()
    {
        var builder = new StringBuilder();

        if (CanReset)
        {
            builder.Append("Вернёт к заводскому: ");
            builder.AppendJoin(", ", Fields.Where(entry => !entry.IsFactory)
                .Select(entry => $"{entry.Caption} → {entry.FactoryLabel}"));
            builder.Append('.');
        }
        else
        {
            builder.Append("Сбрасывать нечего – весь раздел уже на заводских значениях.");
        }

        if (_untouched is not null)
        {
            builder.Append(' ').Append(_untouched);
        }

        return builder.ToString();
    }
}

public sealed class SettingsResetCatalog
{
    private readonly Dictionary<string, SettingsResetPlan> _plans;

    public SettingsResetCatalog(
        IReadOnlyList<PathSettingViewModel> paths,
        ShellPreferences shell,
        ThemeViewModel theme,
        UpdatePreferences updates,
        McpPreferences mcp,
        McpServerOptions mcpOptions)
    {
        var baseDirectory = AppContext.BaseDirectory;

        _plans = new(StringComparer.Ordinal)
        {
            [SettingsSectionKeys.Paths] = new SettingsResetPlan(SettingsSectionKeys.Paths,
                "В файл ничего не пишется – нажмите «Сохранить».",
                [.. paths.Select(path => PathField(path, baseDirectory))]),

            [SettingsSectionKeys.Appearance] = new(SettingsSectionKeys.Appearance,
                null,
                new(ThemeManager.SettingsKeyName, "Тема", "системная",
                    () => theme.Current == AppTheme.System,
                    () => theme.ApplyCommand.Execute(AppTheme.System)),
                new(ShellPreferences.FontScaleKey, "Масштаб шрифта", "100 %",
                    () => Math.Abs(shell.FontScale - FontScaleManager.DefaultScale) < 0.005,
                    () => shell.FontScale = FontScaleManager.DefaultScale),
                new(ShellPreferences.ShowPageHeaderKey, "Заголовок страницы", "показывать",
                    () => shell.ShowPageHeader,
                    () => shell.ShowPageHeader = true)),

            [SettingsSectionKeys.Notifications] = new(SettingsSectionKeys.Notifications,
                null,
                new(ShellPreferences.EnableToastNotificationsKey, "Системные уведомления", "включены",
                    () => shell.EnableToastNotifications,
                    () => shell.EnableToastNotifications = true),
                new(ShellPreferences.ToastOnlyWhenInactiveKey, "Только когда окно неактивно", "включено",
                    () => shell.ToastOnlyWhenInactive,
                    () => shell.ToastOnlyWhenInactive = true)),

            [SettingsSectionKeys.Sync] = new(SettingsSectionKeys.Sync,
                null,
                new(ShellPreferences.AutoSyncEnabledKey, "Автосинхронизация в фоне", "включена",
                    () => shell.AutoSyncEnabled,
                    () => shell.AutoSyncEnabled = true),
                new(ShellPreferences.AutoSyncStartupDelaySecondsKey, "Пауза после запуска", $"{ShellPreferences.DefaultAutoSyncStartupDelaySeconds} с",
                    () => shell.AutoSyncStartupDelaySeconds == ShellPreferences.DefaultAutoSyncStartupDelaySeconds,
                    () => shell.AutoSyncStartupDelaySeconds = ShellPreferences.DefaultAutoSyncStartupDelaySeconds)),

            [SettingsSectionKeys.Updates] = new(SettingsSectionKeys.Updates,
                null,
                new SettingsResetField(UpdatePreferences.RepoKey, "Репозиторий релизов", UpdatePreferences.FactoryRepo,
                    () => updates.IsFactoryRepo,
                    updates.ResetRepo)),

            [SettingsSectionKeys.Mcp] = new(SettingsSectionKeys.Mcp,
                "Токен останется прежним – для него своя кнопка.",
                new(McpPreferences.EnabledKey, "Сервер", "выключен",
                    () => !mcp.Enabled,
                    () => mcp.Enabled = false),
                new(McpPreferences.PortKey, "Порт", mcpOptions.DefaultPort.ToString(),
                    () => mcp.Port == mcpOptions.DefaultPort,
                    () => mcp.Port = mcpOptions.DefaultPort),
                new(McpPreferences.AllowMutationsKey, "Изменяющие операции", "запрещены",
                    () => !mcp.AllowMutations,
                    () => mcp.AllowMutations = false)),
        };
    }

    public SettingsResetPlan this[string sectionKey] => _plans[sectionKey];

    public void Refresh()
    {
        foreach (var plan in _plans.Values)
        {
            plan.Refresh();
        }
    }

    public void EnsureExhaustive(IEnumerable<SettingsSection> sections)
    {
        var keys = sections.Select(section => section.Key).ToArray();
        var missing = keys.Where(key => !_plans.ContainsKey(key)).ToArray();
        var orphaned = _plans.Keys.Where(key => !keys.Contains(key, StringComparer.Ordinal)).ToArray();

        if (missing.Length == 0 && orphaned.Length == 0)
        {
            return;
        }

        var message = new StringBuilder("Каталог сброса разошёлся со списком разделов настроек.");

        if (missing.Length > 0)
        {
            message.Append(" Без плана сброса: ").AppendJoin(", ", missing).Append('.');
        }

        if (orphaned.Length > 0)
        {
            message.Append(" План есть, а раздела нет: ").AppendJoin(", ", orphaned).Append('.');
        }

        throw new InvalidOperationException(message.ToString());
    }

    private static SettingsResetField PathField(PathSettingViewModel path, string baseDirectory)
    {
        return new(path.Key,
            path.Title,
            path.FactoryValue,
            () => SamePath(path.EditValue, path.FactoryValue, baseDirectory),
            () => path.EditValue = path.FactoryValue);
    }

    private static bool SamePath(string? left, string right, string baseDirectory)
    {
        var a = FullPathOrNull(left, baseDirectory);
        var b = FullPathOrNull(right, baseDirectory);

        return a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FullPathOrNull(string? value, string baseDirectory)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed, baseDirectory));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
