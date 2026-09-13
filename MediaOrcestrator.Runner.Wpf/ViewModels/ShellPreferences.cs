using CommunityToolkit.Mvvm.ComponentModel;
using MediaOrcestrator.Runner.Wpf.Bootstrap;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed record BackdropChoice(BackdropKind Kind, string Label);

public sealed partial class ShellPreferences : ShellPreferencesBase
{
    public const string FontScaleKey = "wpf.shell.font_scale";
    public const string ShowPageHeaderKey = "wpf.shell.show_page_header";
    public const string EnableToastNotificationsKey = "wpf.notifications.toast";
    public const string ToastOnlyWhenInactiveKey = "wpf.notifications.toast.only_when_inactive";

    public IReadOnlyList<BackdropChoice> BackdropOptions { get; } =
    [
        new(BackdropKind.Mica, "Mica"),
        new(BackdropKind.MicaAlt, "Mica Alt"),
        new(BackdropKind.Acrylic, "Acrylic"),
        new(BackdropKind.None, "Без эффекта"),
    ];

    [ObservableProperty]
    private BackdropKind _backdrop = BackdropKind.Mica;

    [ObservableProperty]
    private bool _toastOnlyWhenInactive = true;

    public ShellPreferences(ISettingsStore settings)
        : base(settings, new(ShowPageHeaderKey, EnableToastNotificationsKey, FontScaleKey))
    {
        SuppressPersist = true;
        Backdrop = BackdropSettings.Parse(Settings.GetStringValue(BackdropSettings.Key));
        ToastOnlyWhenInactive = LoadBool(ToastOnlyWhenInactiveKey, fallback: true);
        SuppressPersist = false;
    }

    partial void OnBackdropChanged(BackdropKind value)
    {
        if (!SuppressPersist)
        {
            Settings.SetValue(BackdropSettings.Key, BackdropSettings.ToToken(value));
        }
    }

    partial void OnToastOnlyWhenInactiveChanged(bool value) => PersistBool(ToastOnlyWhenInactiveKey, value);
}
