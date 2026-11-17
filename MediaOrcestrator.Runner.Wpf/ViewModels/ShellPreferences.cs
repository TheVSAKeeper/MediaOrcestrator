using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class ShellPreferences : ShellPreferencesBase
{
    public const string FontScaleKey = "wpf.shell.font_scale";
    public const string ShowPageHeaderKey = "wpf.shell.show_page_header";
    public const string EnableToastNotificationsKey = "wpf.notifications.toast";
    public const string ToastOnlyWhenInactiveKey = "wpf.notifications.toast.only_when_inactive";
    public const string AutoSyncEnabledKey = "wpf.sync.auto_enabled";
    public const string AutoSyncStartupDelaySecondsKey = "wpf.sync.startup_delay_seconds";

    public const int DefaultAutoSyncStartupDelaySeconds = 60;
    public const int MinAutoSyncStartupDelaySeconds = 0;
    public const int MaxAutoSyncStartupDelaySeconds = 3600;

    [ObservableProperty]
    private bool _toastOnlyWhenInactive = true;

    [ObservableProperty]
    private bool _autoSyncEnabled = true;

    private int _autoSyncStartupDelaySeconds = DefaultAutoSyncStartupDelaySeconds;

    public ShellPreferences(ISettingsStore settings)
        : base(settings, new(ShowPageHeaderKey, EnableToastNotificationsKey, FontScaleKey))
    {
        SuppressPersist = true;
        ToastOnlyWhenInactive = LoadBool(ToastOnlyWhenInactiveKey, fallback: true);
        AutoSyncEnabled = LoadBool(AutoSyncEnabledKey, fallback: true);
        _autoSyncStartupDelaySeconds = ClampAutoSyncDelay(Settings.GetInt(AutoSyncStartupDelaySecondsKey, DefaultAutoSyncStartupDelaySeconds));
        SuppressPersist = false;
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

    partial void OnToastOnlyWhenInactiveChanged(bool value) => PersistBool(ToastOnlyWhenInactiveKey, value);

    partial void OnAutoSyncEnabledChanged(bool value) => PersistBool(AutoSyncEnabledKey, value);

    private static int ClampAutoSyncDelay(int value) =>
        Math.Clamp(value, MinAutoSyncStartupDelaySeconds, MaxAutoSyncStartupDelaySeconds);
}
