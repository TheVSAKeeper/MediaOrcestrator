using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

public sealed class SettingsResetField
{
    private readonly Func<bool> _isFactory;
    private readonly Action _apply;
    private readonly Action<SettingsResetField> _onReset;

    public SettingsResetField(
        string key,
        string caption,
        string factoryLabel,
        Func<bool> isFactory,
        Action apply,
        Action<SettingsResetField> onReset)
    {
        Key = key;
        Caption = caption;
        FactoryLabel = factoryLabel;
        _isFactory = isFactory;
        _apply = apply;
        _onReset = onReset;
        ResetCommand = new RelayCommand(() => _onReset(this), () => !IsFactory);
    }

    public string Key { get; }

    public string SectionKey { get; internal set; } = string.Empty;

    public string Caption { get; }

    public string FactoryLabel { get; }

    public string Hint => $"Вернуть к заводскому значению: {FactoryLabel}";

    public string AutomationName => $"Сбросить: {Caption}";

    public bool IsFactory => _isFactory();

    public IRelayCommand ResetCommand { get; }

    public void Apply()
    {
        if (!IsFactory)
        {
            _apply();
        }
    }

    public void RefreshAvailability()
    {
        ResetCommand.NotifyCanExecuteChanged();
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

        foreach (var field in fields)
        {
            field.SectionKey = sectionKey;
        }
    }

    public string SectionKey { get; }

    public IReadOnlyList<SettingsResetField> Fields { get; }

    public bool CanReset => Fields.Any(entry => !entry.IsFactory);

    public string Hint => BuildHint();

    public IReadOnlyList<SettingsResetField> Apply()
    {
        var changed = Fields.Where(entry => !entry.IsFactory).ToArray();

        foreach (var entry in changed)
        {
            entry.Apply();
        }

        Refresh();

        return changed;
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
        McpServerOptions mcpOptions,
        Action<SettingsResetField> onFieldReset)
    {
        var baseDirectory = AppContext.BaseDirectory;

        SettingsResetField Field(string key, string caption, string factoryLabel, Func<bool> isFactory, Action apply) =>
            new(key, caption, factoryLabel, isFactory, apply, onFieldReset);

        _plans = new(StringComparer.Ordinal)
        {
            [SettingsSectionKeys.Paths] = new SettingsResetPlan(SettingsSectionKeys.Paths,
                "В файл ничего не пишется – нажмите «Сохранить».",
                [.. paths.Select(path => PathField(path, baseDirectory, onFieldReset))]),

            [SettingsSectionKeys.Appearance] = new(SettingsSectionKeys.Appearance,
                null,
                Field(ThemeManager.SettingsKeyName, "Тема", "системная",
                    () => theme.Current == AppTheme.System,
                    () => theme.ApplyCommand.Execute(AppTheme.System)),
                Field(ShellPreferences.FontScaleKey, "Масштаб шрифта", "100 %",
                    () => Math.Abs(shell.FontScale - FontScaleManager.DefaultScale) < 0.005,
                    () => shell.FontScale = FontScaleManager.DefaultScale),
                Field(ShellPreferences.ShowPageHeaderKey, "Заголовок страницы", "показывать",
                    () => shell.ShowPageHeader,
                    () => shell.ShowPageHeader = true),
                Field(ShellPreferences.StartupSectionKey, "Стартовая страница", SettingsOptions.StartupPageLabelOf(ShellPreferences.DefaultStartupSection),
                    () => string.Equals(shell.StartupSection, ShellPreferences.DefaultStartupSection, StringComparison.Ordinal),
                    () => shell.StartupSection = ShellPreferences.DefaultStartupSection),
                Field(ShellPreferences.NavCollapsedKey, "Свёрнутое боковое меню", "развёрнуто",
                    () => !shell.NavCollapsed,
                    () => shell.NavCollapsed = false)),

            [SettingsSectionKeys.Notifications] = new(SettingsSectionKeys.Notifications,
                null,
                Field(ShellPreferences.EnableToastNotificationsKey, "Уведомления о задачах", "включены",
                    () => shell.EnableToastNotifications,
                    () => shell.EnableToastNotifications = true),
                Field(ShellPreferences.ToastOnlyWhenInactiveKey, "Только когда окно неактивно", "включено",
                    () => shell.ToastOnlyWhenInactive,
                    () => shell.ToastOnlyWhenInactive = true)),

            [SettingsSectionKeys.Sync] = new(SettingsSectionKeys.Sync,
                null,
                Field(ShellPreferences.AutoSyncEnabledKey, "Автосинхронизация в фоне", "включена",
                    () => shell.AutoSyncEnabled,
                    () => shell.AutoSyncEnabled = true),
                Field(ShellPreferences.AutoSyncStartupDelaySecondsKey, "Пауза после запуска", $"{ShellPreferences.DefaultAutoSyncStartupDelaySeconds} с",
                    () => shell.AutoSyncStartupDelaySeconds == ShellPreferences.DefaultAutoSyncStartupDelaySeconds,
                    () => shell.AutoSyncStartupDelaySeconds = ShellPreferences.DefaultAutoSyncStartupDelaySeconds)),

            [SettingsSectionKeys.Updates] = new(SettingsSectionKeys.Updates,
                null,
                Field(UpdatePreferences.RepoKey, "Репозиторий релизов", UpdatePreferences.FactoryRepo,
                    () => updates.IsFactoryRepo,
                    updates.ResetRepo),
                Field(UpdatePreferences.CheckOnStartupKey, "Проверка при запуске", "включена",
                    () => updates.CheckOnStartup,
                    () => updates.CheckOnStartup = true)),

            [SettingsSectionKeys.Mcp] = new(SettingsSectionKeys.Mcp,
                "Токен останется прежним – для него своя кнопка.",
                Field(McpPreferences.EnabledKey, "Сервер", "выключен",
                    () => !mcp.Enabled,
                    () => mcp.Enabled = false),
                Field(McpPreferences.PortKey, "Порт", mcpOptions.DefaultPort.ToString(),
                    () => mcp.Port == mcpOptions.DefaultPort,
                    () => mcp.Port = mcpOptions.DefaultPort),
                Field(McpPreferences.AllowMutationsKey, "Изменяющие операции", "запрещены",
                    () => !mcp.AllowMutations,
                    () => mcp.AllowMutations = false)),
        };

        Fields = _plans.Values
            .SelectMany(plan => plan.Fields)
            .ToDictionary(field => field.Key, StringComparer.Ordinal);
    }

    public SettingsResetPlan this[string sectionKey] => _plans[sectionKey];

    public IReadOnlyDictionary<string, SettingsResetField> Fields { get; }

    public void Refresh()
    {
        foreach (var plan in _plans.Values)
        {
            plan.Refresh();
        }

        foreach (var field in Fields.Values)
        {
            field.RefreshAvailability();
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

    private static SettingsResetField PathField(
        PathSettingViewModel path,
        string baseDirectory,
        Action<SettingsResetField> onFieldReset)
    {
        var field = new SettingsResetField(
            path.Key,
            path.Title,
            path.FactoryValue,
            () => SamePath(path.EditValue, path.FactoryValue, baseDirectory),
            () => path.EditValue = path.FactoryValue,
            onFieldReset);

        path.ResetField = field;

        return field;
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
