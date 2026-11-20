using KeepShell.Automation;
using KeepShell.Diagnostics;
using KeepShell.Mcp;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Migration;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Bootstrap.Gallery;
using MediaOrcestrator.Runner.Wpf.Mcp;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.Views;
using ErrorReportWindow = KeepShell.Views.ErrorReportWindow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace MediaOrcestrator.Runner.Wpf;

public partial class App : Application
{
    private const int ExpectedStartupSpans = 6;
    private const int LogSinkCapacity = 1217;
    private const string LogFilePrefix = "wpf-";

    private IHost? _host;
    private readonly WpfLogSink _logSink = new(LogSinkCapacity);
    private readonly SingleInstanceMessenger _messenger = new("MediaOrcestrator.Wpf.IPC");
    private SingleInstanceGuard? _singleInstanceGuard;
    private GlobalExceptionHandlers? _exceptionHandlers;
    private StartupSplash? _splash;
    private WpfBindingJournal? _bindingJournal;
    private IDisposable? _systemThemeWatch;
    private CancellationTokenSource? _ipcCts;
    private string? _startupCommand;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        StyledMessageBox.DefaultTitle = "MediaOrcestrator";
        AppResources.InstallInto(this);

        var galleryIndex = Array.FindIndex(e.Args, static arg => string.Equals(arg, GalleryOptions.Argument, StringComparison.OrdinalIgnoreCase));

        if (galleryIndex >= 0)
        {
            await RunGalleryAsync(e.Args.Skip(galleryIndex + 1));
            return;
        }

        var applyImportPid = StartupCommandLine.TryGetApplyImportPid(e.Args);
        if (applyImportPid is int previousPid && previousPid != Environment.ProcessId)
        {
            await Task.Run(() => StartupImportCoordinator.WaitForPreviousProcessExit(previousPid, TimeSpan.FromSeconds(30)));
        }

        _startupCommand = StartupCommandLine.ToCommand(e.Args);
        _singleInstanceGuard = applyImportPid.HasValue
            ? await Task.Run(() => AcquireSingleInstanceWithRestartGrace("MediaOrcestrator", TimeSpan.FromSeconds(10)))
            : SingleInstanceGuard.Acquire("MediaOrcestrator");

        if (!_singleInstanceGuard.IsPrimaryInstance)
        {
            if (_startupCommand is not null && _messenger.Send(_startupCommand))
            {
                Shutdown(0);
                return;
            }

            StyledMessageBox.Show(
                "Медиа оркестратор уже запущен.",
                "Медиа оркестратор",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown(0);
            return;
        }

        _ipcCts = new();
        _messenger.StartServer(OnRemoteCommand, _ipcCts.Token);

        _exceptionHandlers = new(() => _host?.Services.GetService<ErrorReportService>());
        _exceptionHandlers.Attach(this);

        try
        {
            var startupWatch = Stopwatch.StartNew();
            var baseDir = AppContext.BaseDirectory;
            var loggingOptions = BuildLoggingOptions(Path.Combine(baseDir, "logs"));
            var configuration = new LoggerConfiguration();
            ConfigureSerilog(configuration, loggingOptions);
            Log.Logger = configuration.CreateLogger();
            _bindingJournal = new(Log.ForContext<WpfBindingJournal>());

            using var startupLogs = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddSerilog(Log.Logger));
            var importLogger = startupLogs.CreateLogger(typeof(WinFormsPendingImportApplier).FullName!);
            var pluginLogger = startupLogs.CreateLogger(typeof(PluginLoader).FullName!);
            var hadLegacySettingsTxt = File.Exists(Path.Combine(baseDir, TomlSettingsFile.LegacyFileName));
            var appliedImportOutcome = WinFormsPendingImportApplier.ApplyIfPending(baseDir, importLogger);
            if (appliedImportOutcome?.Status == WinFormsPendingImportOutcomeStatus.RollbackFailed)
            {
                StyledMessageBox.Show(
                    appliedImportOutcome.Message,
                    "Критическая ошибка переноса данных",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(1);
                return;
            }

            var paths = PathsLoader.Load();
            var settingsPath = Path.Combine(baseDir, TomlSettingsFile.PrimaryFileName);

            _host = Host.CreateDefaultBuilder()
                .UseSerilog(Log.Logger)
                .ConfigureServices(services => ServiceRegistration.AddMediaOrcestrator(services, paths, settingsPath, _logSink, loggingOptions, pluginLogger))
                .Build();

            Log.Information("MediaOrcestrator.Runner.Wpf запускается...");
            var updaterCheck = UpdaterLogChecker.Check();

            var restoreLogger = _host.Services.GetRequiredService<ILogger<DatabaseBackupService>>();
            var restored = DatabaseBackupService.ApplyPendingRestoreIfAny(paths.DatabasePath, restoreLogger);

            if (restored is not null)
            {
                Log.Information("База данных восстановлена из бэкапа {FileName} до старта DI", restored);
            }

            var settingsStore = _host.Services.GetRequiredService<ISettingsStore>();
            AppThemes.Register();
            var savedKey = settingsStore.GetStringValue(ThemeManager.SettingsKeyName);
            ThemeManager.Apply(string.IsNullOrWhiteSpace(savedKey) ? AppThemes.SystemKey : savedKey);
            _systemThemeWatch = SystemTheme.Watch(Dispatcher);

            FontScaleManager.Initialize(settingsStore.GetDouble(ShellPreferences.FontScaleKey, FontScaleManager.DefaultScale));

            ViewLocator.InstallIntoApplication();

            var version = ResolveAppVersion();
            _splash = new("Медиа оркестратор", version, "Запуск приложения", ExpectedStartupSpans,
                _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<StartupSplash>());

            using (_splash.StartSpan("Запуск хоста..."))
            {
                await _host.StartAsync();
            }

            var orcestrator = _host.Services.GetRequiredService<Orcestrator>();

            using (_splash.StartSpan("Загрузка плагинов..."))
            {
                await Task.Run(orcestrator.Init);
            }

            using (_splash.StartSpan("Резервное копирование базы данных..."))
            {
                var backupService = _host.Services.GetRequiredService<DatabaseBackupService>();
                backupService.ValidateLog();
                backupService.Backup(BackupTrigger.Startup);
                backupService.StartScheduled(TimeSpan.FromHours(6));
            }

            using (_splash.StartSpan("Подготовка временных файлов..."))
            {
                var tempManager = _host.Services.GetRequiredService<TempManager>();
                tempManager.MigrateOldTempPaths();
                tempManager.CleanAll();
            }

            using (_splash.StartSpan("Подготовка состояния источников..."))
            {
                var stateManager = _host.Services.GetRequiredService<StateManager>();
                stateManager.MigrateLegacyStatePaths();
            }

            using (_splash.StartSpan("Открытие главного окна..."))
            {
                var window = _host.Services.GetRequiredService<MainWindow>();
                MainWindow = window;
                window.Show();
            }

            _host.Services.GetRequiredService<PerformanceMonitor>().ReportStartup(startupWatch.Elapsed);

            _splash.Dispose();
            _splash = null;

            JumpListBuilder.Apply();

            _host.Services.GetRequiredService<McpServerHost>().Apply();

            if (_startupCommand is not null)
            {
                _host.Services.GetRequiredService<ShellViewModel>().HandleStartupCommand(_startupCommand);
            }

            StartupPreloader.StartInBackground(_host.Services, Dispatcher);

            if (updaterCheck.HasErrors)
            {
                ShowUpdaterErrorWarning(updaterCheck);
            }

            var importOutcome = WinFormsPendingImportApplier.TryReadAndConsumeResult(baseDir, importLogger) ?? appliedImportOutcome;
            StartupImportCoordinator.HandlePostStartupOutcomeOrPrompt(
                _host.Services,
                importOutcome,
                updaterCheck.HasErrors,
                applyImportPid.HasValue,
                hadLegacySettingsTxt);
        }
        catch (Exception ex)
        {
            _splash?.Dispose();
            _splash = null;

            Log.Fatal(ex, "MediaOrcestrator.Runner.Wpf не смог запуститься");

            try
            {
                var service = _host?.Services.GetService<ErrorReportService>();

                if (service is not null)
                {
                    var payload = service.Build(ex, "Падение на старте");
                    var window = new ErrorReportWindow(payload);
                    window.ShowDialog();
                }
                else
                {
                    StyledMessageBox.Show(ex.ToString(), "MediaOrcestrator – startup failure", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch
            {
                StyledMessageBox.Show(ex.ToString(), "MediaOrcestrator – startup failure", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            try
            {
                await Log.CloseAndFlushAsync();
            }
            catch
            {
            }

            Shutdown(1);
        }
    }

    private void ShowUpdaterErrorWarning(UpdaterLogCheck check)
    {
        var headline = check.RolledBack
            ? "Прошлая попытка обновления сорвалась, вернули предыдущую версию."
            : "Прошлая попытка обновления завершилась с ошибкой.";

        var details = string.IsNullOrWhiteSpace(check.ErrorSummary)
            ? string.Empty
            : $"\n\nПричина: {check.ErrorSummary}";

        var message = $"{headline}{details}\n\nПолный лог сохранён в разделе «Логи».";

        try
        {
            _host?.Services.GetRequiredService<IDialogService>().Warning("Обновление не удалось", message);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Не удалось показать предупреждение об ошибке обновления");
        }
    }

    private void OnRemoteCommand(string command)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var shell = _host?.Services.GetService<ShellViewModel>();
            shell?.HandleStartupCommand(command);

            var window = MainWindow;

            if (window is null)
            {
                return;
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
            window.Focus();
        });
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            try
            {
                _ipcCts?.Cancel();
                _ipcCts?.Dispose();
                _ipcCts = null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Не удалось остановить обработчик команд от другого экземпляра");
            }

            if (_host is not null)
            {
                try
                {
                    var backupService = _host.Services.GetService<DatabaseBackupService>();
                    backupService?.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Ошибка при остановке DatabaseBackupService");
                }

                await _host.StopAsync();
                _host.Dispose();
                _host = null;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка при остановке хоста WPF");
        }
        finally
        {
            _systemThemeWatch?.Dispose();
            _systemThemeWatch = null;

            _singleInstanceGuard?.Dispose();
            _singleInstanceGuard = null;

            _bindingJournal?.Dispose();
            await Log.CloseAndFlushAsync();
            base.OnExit(e);
        }
    }

    private static KeepShellLoggingOptions BuildLoggingOptions(string logsDirectory)
    {
        return new()
        {
            LogsDirectory = logsDirectory,
            FileNamePrefix = LogFilePrefix,
            RetainedFileCountLimit = 30,
            SinkCapacity = LogSinkCapacity,
            MinimumLevelOverrides = new Dictionary<string, LogEventLevel>(StringComparer.Ordinal)
            {
                ["Microsoft"] = LogEventLevel.Warning,
                ["Microsoft.Extensions.Http"] = LogEventLevel.Warning,
                ["System.Net.Http.HttpClient"] = LogEventLevel.Warning,
            },
        };
    }

    private void ConfigureSerilog(LoggerConfiguration configuration, KeepShellLoggingOptions options)
    {
        Directory.CreateDirectory(options.LogsDirectory);

        configuration
            .MinimumLevel.Is(options.MinimumLevel)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: options.OutputTemplate)
            .WriteTo.Debug()
            .WriteTo.Sink(_logSink)
            .WriteTo.File(
                Path.Combine(options.LogsDirectory, $"{options.FileNamePrefix}.log"),
                outputTemplate: options.OutputTemplate,
                rollingInterval: options.RollingInterval,
                retainedFileCountLimit: options.RetainedFileCountLimit);

        foreach (var (source, level) in options.MinimumLevelOverrides ?? new Dictionary<string, LogEventLevel>())
        {
            configuration.MinimumLevel.Override(source, level);
        }
    }

    private async Task RunGalleryAsync(IEnumerable<string> args)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        AppThemes.Register();

        var exitCode = 1;
        GalleryFixture? fixture = null;

        try
        {
            var options = GalleryOptions.Parse(args, Path.Combine(AppContext.BaseDirectory, GalleryRunner.FolderName));
            fixture = GalleryFixture.Create(options);
            var loggingOptions = BuildLoggingOptions(Path.Combine(fixture.Root, "logs"));

            _host = Host.CreateDefaultBuilder()
                .UseSerilog((_, _, configuration) => ConfigureSerilog(configuration, loggingOptions))
                .ConfigureServices(services => ServiceRegistration.AddMediaOrcestrator(services, fixture.Paths, fixture.SettingsPath, _logSink, loggingOptions))
                .Build();

            ThemeManager.Apply(AppThemes.LightKey);
            FontScaleManager.Initialize(options.Arguments.FontScale);
            ViewLocator.InstallIntoApplication();

            await Task.Run(_host.Services.GetRequiredService<Orcestrator>().Init);

            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            var host = GalleryHost.Create(_host.Services, fixture, options, logger);

            exitCode = await GalleryRunner.RunAsync(host, options.Arguments, new GalleryJournal(logger));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Галерея не отрисована");
        }
        finally
        {
            if (_host is not null)
            {
                try
                {
                    await _host.StopAsync();
                    _host.Dispose();
                }
                catch
                {
                }

                _host = null;
            }

            await Log.CloseAndFlushAsync();
            fixture?.Dispose();
        }

        Shutdown(exitCode);
    }

    internal static string ResolveAppVersion()
    {
        var assembly = typeof(App).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "–";
    }

    private static SingleInstanceGuard AcquireSingleInstanceWithRestartGrace(string mutexName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            var guard = SingleInstanceGuard.Acquire(mutexName);
            if (guard.IsPrimaryInstance || DateTime.UtcNow >= deadline)
            {
                return guard;
            }

            guard.Dispose();
            Thread.Sleep(200);
        }
    }
}
