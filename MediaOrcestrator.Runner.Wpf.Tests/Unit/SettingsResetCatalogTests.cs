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
using System.Windows.Threading;

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

    [Test]
    public void ResetField_ПриИзмененииОтдельногоПоля_СбрасываетТолькоЕгоИПоказываетТост()
    {
        var toasts = _provider.GetRequiredService<ToastHostViewModel>();
        _viewModel.Preferences.FontScale = 1.3;
        _viewModel.Preferences.ShowPageHeader = false;

        var scaleField = _viewModel.ResetFields[ShellPreferences.FontScaleKey];
        var headerField = _viewModel.ResetFields[ShellPreferences.ShowPageHeaderKey];

        Assert.That(scaleField.ResetCommand.CanExecute(null), Is.True);

        scaleField.ResetCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_viewModel.Preferences.FontScale, Is.EqualTo(FontScaleManager.DefaultScale).Within(0.001));
            Assert.That(_viewModel.Preferences.ShowPageHeader, Is.False);
            Assert.That(scaleField.ResetCommand.CanExecute(null), Is.False);
            Assert.That(headerField.ResetCommand.CanExecute(null), Is.True);
            Assert.That(_viewModel.Reset[SettingsSectionKeys.Appearance].CanReset, Is.True);
            Assert.That(toasts.Toasts.Count, Is.EqualTo(1));
        }
    }

    [Test]
    public void PathSetting_ResetField_СвязаноСКнопкойСтрокиИНеПишетВStore()
    {
        var pathSetting = _viewModel.Paths[0];
        var customPath = Path.Combine(_ctx.Root, "custom-plugins");
        Directory.CreateDirectory(customPath);
        pathSetting.EditValue = customPath;

        Assert.That(pathSetting.ResetField, Is.Not.Null);
        Assert.That(pathSetting.ResetField!.ResetCommand.CanExecute(null), Is.True);

        pathSetting.ResetField.ResetCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pathSetting.EditValue, Is.EqualTo(pathSetting.FactoryValue));
            Assert.That(_ctx.Settings.GetStringValue(pathSetting.Key), Is.Null);
            Assert.That(pathSetting.ResetField.ResetCommand.CanExecute(null), Is.False);
        }
    }

    [Test]
    public void McpConnectSnippet_ПриСменеФормата_ВозвращаетВсеПятьФорматов()
    {
        var snippets = Enumerable.Range(0, _viewModel.McpConnectFormats.Count)
            .Select(index =>
            {
                _viewModel.SelectedMcpFormatIndex = index;
                return _viewModel.McpConnectSnippet;
            })
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snippets, Has.Length.EqualTo(5));
            Assert.That(snippets.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(5));
            Assert.That(snippets[0], Does.Contain("mcpServers"));
            Assert.That(snippets[1], Does.Contain("claude mcp add"));
        }
    }

    [Test]
    public void ResetSection_ПриИзменённыхЗапускеИАвтопроверкеОбновлений_ВозвращаетЗаводскиеЗначения()
    {
        var transferOption = _viewModel.StartupOptions.First(o => o.Key == "transfers");
        _viewModel.SelectedStartupOption = transferOption;
        _viewModel.Preferences.NavCollapsed = true;
        _viewModel.Updates.CheckOnStartup = false;

        _viewModel.ResetSectionCommand.Execute(_viewModel.Reset[SettingsSectionKeys.Appearance]);
        _viewModel.ResetSectionCommand.Execute(_viewModel.Reset[SettingsSectionKeys.Updates]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_viewModel.Preferences.StartupSection, Is.EqualTo(ShellPreferences.DefaultStartupSection));
            Assert.That(_viewModel.SelectedStartupOption.Key, Is.EqualTo(ShellPreferences.DefaultStartupSection));
            Assert.That(_viewModel.Preferences.NavCollapsed, Is.False);
            Assert.That(_viewModel.Updates.CheckOnStartup, Is.True);
            Assert.That(_ctx.Settings.GetStringValue(ShellPreferences.StartupSectionKey), Is.EqualTo(ShellPreferences.DefaultStartupSection));
            Assert.That(_ctx.Settings.GetStringValue(ShellPreferences.NavCollapsedKey), Is.EqualTo("false"));
            Assert.That(_ctx.Settings.GetStringValue(UpdatePreferences.CheckOnStartupKey), Is.EqualTo("true"));
        }
    }

    [Test]
    public void StartupPreloader_BuildIoSteps_ПропускаетПроверкуОбновленийПриВыключеннойНастройке()
    {
        var tools = _provider.GetRequiredService<ToolsViewModel>();
        var updateManager = _provider.GetRequiredService<AppUpdateManager>();
        var appUpdate = _provider.GetRequiredService<AppUpdateViewModel>();

        _viewModel.Updates.CheckOnStartup = true;
        var enabledSteps = StartupPreloader.BuildIoSteps(tools, updateManager, appUpdate, _viewModel.Updates, Dispatcher.CurrentDispatcher, _ => { });

        _viewModel.Updates.CheckOnStartup = false;
        var disabledSteps = StartupPreloader.BuildIoSteps(tools, updateManager, appUpdate, _viewModel.Updates, Dispatcher.CurrentDispatcher, _ => { });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(enabledSteps.Select(s => s.Status), Does.Contain("Проверка обновлений приложения"));
            Assert.That(disabledSteps.Select(s => s.Status), Does.Not.Contain("Проверка обновлений приложения"));
            Assert.That(disabledSteps, Has.Length.EqualTo(enabledSteps.Length - 1));
        }
    }
}
