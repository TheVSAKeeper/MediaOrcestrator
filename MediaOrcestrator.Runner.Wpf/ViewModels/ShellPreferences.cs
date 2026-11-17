using CommunityToolkit.Mvvm.ComponentModel;
using MediaOrcestrator.Runner.Wpf.ViewModels.Settings;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class ShellPreferences : ShellPreferencesBase
{
    public const string FontScaleKey = "wpf.shell.font_scale";
    public const string ShowPageHeaderKey = "wpf.shell.show_page_header";
    public const string NavCollapsedKey = "wpf.shell.nav_collapsed";
    public const string StartupSectionKey = "wpf.shell.startup_page";
    public const string LastSectionKey = "wpf.shell.last_page";
    public const string EnableToastNotificationsKey = "wpf.notifications.toast";
    public const string ToastOnlyWhenInactiveKey = "wpf.notifications.toast.only_when_inactive";
    public const string AutoSyncEnabledKey = "wpf.sync.auto_enabled";
    public const string AutoSyncStartupDelaySecondsKey = "wpf.sync.startup_delay_seconds";

    public const string DefaultStartupSection = "media";
    public const string LastUsedStartupSection = "last";
    public const int DefaultAutoSyncStartupDelaySeconds = 60;
    public const int MinAutoSyncStartupDelaySeconds = 0;
    public const int MaxAutoSyncStartupDelaySeconds = 3600;

    [ObservableProperty]
    private bool _navCollapsed;

    private string _startupSection = DefaultStartupSection;

    [ObservableProperty]
    private bool _toastOnlyWhenInactive = true;

    [ObservableProperty]
    private bool _autoSyncEnabled = true;

    private int _autoSyncStartupDelaySeconds = DefaultAutoSyncStartupDelaySeconds;

    public ShellPreferences(ISettingsStore settings)
        : base(settings, new(ShowPageHeaderKey, EnableToastNotificationsKey, FontScaleKey))
    {
        SuppressPersist = true;
        NavCollapsed = LoadBool(NavCollapsedKey, fallback: false);
        _startupSection = NormalizeStartupSection(Settings.GetStringValue(StartupSectionKey));
        ToastOnlyWhenInactive = LoadBool(ToastOnlyWhenInactiveKey, fallback: true);
        AutoSyncEnabled = LoadBool(AutoSyncEnabledKey, fallback: true);
        _autoSyncStartupDelaySeconds = ClampAutoSyncDelay(Settings.GetInt(AutoSyncStartupDelaySecondsKey, DefaultAutoSyncStartupDelaySeconds));
        SuppressPersist = false;
    }

    public string StartupSection
    {
        get => _startupSection;
        set
        {
            var normalized = NormalizeStartupSection(value);

            if (!SetProperty(ref _startupSection, normalized))
            {
                return;
            }

            if (!SuppressPersist)
            {
                Settings.SetValue(StartupSectionKey, normalized);
            }
        }
    }

    public string LastSection
    {
        get
        {
            var raw = Settings.GetStringValue(LastSectionKey)?.Trim();
            return string.IsNullOrEmpty(raw) ? DefaultStartupSection : raw;
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            Settings.SetValue(LastSectionKey, value.Trim());
        }
    }

    public int AutoSyncStartupDelaySeconds
    {
        get => _autoSyncStartupDelaySeconds;
        set
        {
            var clamped = ClampAutoSyncDelay(value);

            if (!SetProperty(ref _autoSyncStartupDelaySeconds, clamped))
            {
                if (clamped != value)
                {
                    OnPropertyChanged();
                }

                return;
            }

            if (!SuppressPersist)
            {
                Settings.SetInt(AutoSyncStartupDelaySecondsKey, clamped);
            }
        }
    }

    partial void OnNavCollapsedChanged(bool value) => PersistBool(NavCollapsedKey, value);

    partial void OnToastOnlyWhenInactiveChanged(bool value) => PersistBool(ToastOnlyWhenInactiveKey, value);

    partial void OnAutoSyncEnabledChanged(bool value) => PersistBool(AutoSyncEnabledKey, value);

    private static string NormalizeStartupSection(string? raw)
    {
        var trimmed = raw?.Trim();
        return SettingsOptions.IsValidStartupPage(trimmed) ? trimmed! : DefaultStartupSection;
    }

    private static int ClampAutoSyncDelay(int value) =>
        Math.Clamp(value, MinAutoSyncStartupDelaySeconds, MaxAutoSyncStartupDelaySeconds);
}
