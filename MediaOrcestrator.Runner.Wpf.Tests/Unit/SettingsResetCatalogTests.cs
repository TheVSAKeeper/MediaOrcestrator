using KeepShell.Bootstrap;
using KeepShell.Mcp;
using KeepShell.Services;
using KeepShell.ViewModels;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class SettingsResetCatalogTests
{
    private TestDomainContext _ctx = null!;
    private ServiceProvider _provider = null!;
    private SettingsViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new();

        var services = new ServiceCollection();
        services.AddLogging();
        ServiceRegistration.AddMediaOrcestrator(
            services,
            _ctx.Paths,
            _ctx.Settings,
            new WpfLogSink(16),
            new KeepShellLoggingOptions { LogsDirectory = Path.Combine(_ctx.Root, "logs") });
        services.AddSingleton(_ctx.Dialogs);
        services.AddSingleton(_ctx.FilePicker);
        services.AddSingleton(_ctx.Clipboard);
        services.AddSingleton(_ctx.ShellLauncher);
        services.AddSingleton(_ctx.Lifetime);

        _provider = services.BuildServiceProvider();
        _viewModel = _provider.GetRequiredService<SettingsViewModel>();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        _ctx.Dispose();
    }

    [Test]
    public void EnsureExhaustive_НаРеальныхРазделахSettingsViewModel_НеБросаетИсключение()
    {
        Assert.DoesNotThrow(() => _viewModel.Reset.EnsureExhaustive(_viewModel.Sections.Items));
    }

    [Test]
    public void EnsureExhaustive_ПриОтсутствующемИлиЛишнемРазделе_БросаетInvalidOperationException()
    {
        var withExtra = _viewModel.Sections.Items
            .Append(new SettingsSection("unknown", "Лишний", MahApps.Metro.IconPacks.PackIconLucideKind.Bug, string.Empty))
            .ToArray();

        var withMissing = _viewModel.Sections.Items.Skip(1).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<InvalidOperationException>(() => _viewModel.Reset.EnsureExhaustive(withExtra));
            Assert.Throws<InvalidOperationException>(() => _viewModel.Reset.EnsureExhaustive(withMissing));
        }
    }

    [Test]
    public void ResetSection_ПослеИзмененияПутиИМасштабаШрифта_ВозвращаетЗаводскиеЗначения()
    {
        var pathSetting = _viewModel.Paths[0];
        var customPath = Path.Combine(_ctx.Root, "custom-plugins");
        Directory.CreateDirectory(customPath);
        pathSetting.EditValue = customPath;

        _viewModel.Preferences.FontScale = 1.4;

        _viewModel.ResetSectionCommand.Execute(_viewModel.Reset[SettingsSectionKeys.Paths]);
        _viewModel.ResetSectionCommand.Execute(_viewModel.Reset[SettingsSectionKeys.Appearance]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pathSetting.EditValue, Is.EqualTo(pathSetting.FactoryValue));
            Assert.That(_viewModel.Preferences.FontScale, Is.EqualTo(FontScaleManager.DefaultScale).Within(0.001));
            Assert.That(_viewModel.Reset[SettingsSectionKeys.Paths].CanReset, Is.False);
            Assert.That(_viewModel.Reset[SettingsSectionKeys.Appearance].CanReset, Is.False);
        }
    }
}
