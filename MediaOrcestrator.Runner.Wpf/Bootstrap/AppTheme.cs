using KeepShell.Bootstrap;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

public enum AppTheme
{
    None = 0,
    Light = 1,
    Dark = 2,
    Tarkov = 3,
}

public static class AppThemes
{
    public const string LightKey = "light";
    public const string DarkKey = "dark";
    public const string TarkovKey = TarkovTheme.Key;

    public static string ToKey(AppTheme theme) => theme switch
    {
        AppTheme.Dark => DarkKey,
        AppTheme.Tarkov => TarkovKey,
        _ => LightKey,
    };

    public static AppTheme FromKey(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        DarkKey => AppTheme.Dark,
        TarkovKey => AppTheme.Tarkov,
        _ => AppTheme.Light,
    };

    public static void Register()
    {
        ThemeManager.Register(ThemeManager.DefaultLight);
        ThemeManager.Register(ThemeManager.DefaultDark);

        TarkovTheme.Register();
    }
}
