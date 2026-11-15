using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class ShellPreferences : ShellPreferencesBase
{
    public const string FontScaleKey = "wpf.shell.font_scale";
    public const string ShowPageHeaderKey = "wpf.shell.show_page_header";
    public const string EnableToastNotificationsKey = "wpf.notifications.toast";
    public const string ToastOnlyWhenInactiveKey = "wpf.notifications.toast.only_when_inactive";

    [ObservableProperty]
    private bool _toastOnlyWhenInactive = true;

    public ShellPreferences(ISettingsStore settings)
        : base(settings, new(ShowPageHeaderKey, EnableToastNotificationsKey, FontScaleKey))
    {
        SuppressPersist = true;
        ToastOnlyWhenInactive = LoadBool(ToastOnlyWhenInactiveKey, fallback: true);
        SuppressPersist = false;
    }

    partial void OnToastOnlyWhenInactiveChanged(bool value) => PersistBool(ToastOnlyWhenInactiveKey, value);
}
