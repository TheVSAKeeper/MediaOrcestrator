using System.Windows;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

public static class AppResources
{
    public static readonly Uri[] Sources =
    [
        new("pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml"),
        new("pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml"),
        new("pack://application:,,,/KeepShell;component/Resources/Converters.xaml"),
        new("pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml"),
        new("pack://application:,,,/MediaOrcestrator.Runner.Wpf;component/Resources/Converters.xaml"),
        new("pack://application:,,,/MediaOrcestrator.Runner.Wpf;component/Resources/Styles/AppCards.xaml"),
        new("pack://application:,,,/MediaOrcestrator.Runner.Wpf;component/Resources/ViewOverrides.xaml"),
    ];

    public static void InstallInto(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        foreach (var source in Sources)
        {
            application.Resources.MergedDictionaries.Add(
                new()
                {
                    Source = source,
                });
        }
    }
}
