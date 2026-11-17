using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class ShellThemeBootResetTests
{
    private TestDomainContext _ctx = null!;
    private ServiceProvider _provider = null!;
    private ShellViewModel _shell = null!;

    [SetUp]
    public void SetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
        AppThemes.Register();
        ThemeManager.Apply(AppThemes.LightKey);

        _ctx = new();

        var services = new ServiceCollection();
        services.AddLogging();
        ServiceRegistration.AddMediaOrcestrator(
            services,
            _ctx.Paths,
            _ctx.Settings,
            new WpfLogSink(16),
            new KeepShellLoggingOptions { LogsDirectory = Path.Combine(_ctx.Root, "logs") });
        services.AddSingleton<IUiDispatcher>(_ctx.Dispatcher);
        services.AddSingleton<IPageRenderProbe>(_ctx.RenderProbe);
        services.AddSingleton(_ctx.Dialogs);

        _provider = services.BuildServiceProvider();
        _shell = _provider.GetRequiredService<ShellViewModel>();
    }

    [TearDown]
    public void TearDown()
    {
        ThemeManager.Apply(AppThemes.LightKey);
        _provider.Dispose();
        _ctx.Dispose();
    }

    [Test]
    public void OnThemePropertyChanged_ПриПереключенииСТарковНаДругуюТему_СбрасываетIsTarkovBootPlaying()
    {
        _shell.Theme.ApplyCommand.Execute(AppTheme.Tarkov);
        var playingInTarkov = _shell.IsTarkovBootPlaying;

        _shell.Theme.ApplyCommand.Execute(AppTheme.Dark);
        var playingInDark = _shell.IsTarkovBootPlaying;

        _shell.Theme.ApplyCommand.Execute(AppTheme.Tarkov);
        var playingInTarkovAgain = _shell.IsTarkovBootPlaying;

        _shell.Theme.ApplyCommand.Execute(AppTheme.Light);
        var playingInLight = _shell.IsTarkovBootPlaying;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(playingInTarkov, Is.True);
            Assert.That(playingInDark, Is.False);
            Assert.That(playingInTarkovAgain, Is.True);
            Assert.That(playingInLight, Is.False);
        }
    }
}
