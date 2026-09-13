using System.Windows;

namespace MediaOrcestrator.Runner.Wpf.Services;

public sealed class AppLifetime : IAppLifetime
{
    public void Shutdown() => Application.Current?.Shutdown();
}
