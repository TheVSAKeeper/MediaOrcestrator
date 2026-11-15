using Microsoft.Win32;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

public static class SystemTheme
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValueName = "AppsUseLightTheme";

    public static bool AppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            var value = key?.GetValue(AppsUseLightThemeValueName);

            return value is not int intValue || intValue != 0;
        }
        catch
        {
            return true;
        }
    }

    public static IDisposable Watch(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        return new Watcher(dispatcher);
    }

    private sealed class Watcher : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private bool _disposed;

        public Watcher(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General || _disposed)
            {
                return;
            }

            _dispatcher.BeginInvoke(() =>
            {
                if (!_disposed)
                {
                    AppThemes.FollowSystem(AppsUseLightTheme());
                }
            });
        }
    }
}
