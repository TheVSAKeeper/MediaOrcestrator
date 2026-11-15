using KeepShell.Bootstrap;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

public enum AppTheme
{
    None = 0,
    Light = 1,
    Dark = 2,
    Tarkov = 3,
    System = 4,
}

public static class AppThemes
{
    public const string LightKey = "light";
    public const string DarkKey = "dark";
    public const string TarkovKey = TarkovTheme.Key;
    public const string SystemKey = "system";

    public static string ToKey(AppTheme theme) => theme switch
    {
        AppTheme.Light => LightKey,
        AppTheme.Dark => DarkKey,
        AppTheme.Tarkov => TarkovKey,
        _ => SystemKey,
    };

    public static AppTheme FromKey(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        LightKey => AppTheme.Light,
        DarkKey => AppTheme.Dark,
        TarkovKey => AppTheme.Tarkov,
        _ => AppTheme.System,
    };

    public static void Register()
    {
        ThemeManager.Register(ThemeManager.DefaultLight);
        ThemeManager.Register(ThemeManager.DefaultDark);

        TarkovTheme.Register();
        RegisterSystem(SystemTheme.AppsUseLightTheme());
    }

    public static void RegisterSystem(bool appsUseLightTheme)
    {
        var source = appsUseLightTheme ? ThemeManager.DefaultLight : ThemeManager.DefaultDark;
        ThemeManager.Register(
            source with
            {
                Key = SystemKey,
            });
    }

    public static void FollowSystem(bool appsUseLightTheme)
    {
        if (ThemeManager.Find(SystemKey)?.IsDark == !appsUseLightTheme)
        {
            return;
        }

        RegisterSystem(appsUseLightTheme);

        if (!string.Equals(ThemeManager.Current?.Key, SystemKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ThemeManager.Apply(SystemKey);
    }
}
