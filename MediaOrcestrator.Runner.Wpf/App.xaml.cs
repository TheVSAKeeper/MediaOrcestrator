using LiteDB;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Domain.Merging;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.Views;
using ErrorReportWindow = KeepShell.Views.ErrorReportWindow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
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
    private CancellationTokenSource? _ipcCts;
    private string? _startupCommand;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        StyledMessageBox.DefaultTitle = "MediaOrcestrator";


        _startupCommand = StartupCommandLine.ToCommand(e.Args);
        _singleInstanceGuard = SingleInstanceGuard.Acquire("MediaOrcestrator");

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
            var paths = PathsLoader.Load();
            var settingsPath = Path.Combine(AppContext.BaseDirectory, TomlSettingsFile.PrimaryFileName);

            var loggingOptions = BuildLoggingOptions(Path.Combine(AppContext.BaseDirectory, "logs"));

            _host = Host.CreateDefaultBuilder()
                .UseSerilog((_, _, configuration) => ConfigureSerilog(configuration, loggingOptions))
                .ConfigureServices(services => ConfigureServices(services, paths, settingsPath, _logSink, loggingOptions))
                .Build();

            Log.Information("MediaOrcestrator.Runner.Wpf запускается...");
            UpdaterLogChecker.Check();

            var restoreLogger = _host.Services.GetRequiredService<ILogger<DatabaseBackupService>>();
            var restored = DatabaseBackupService.ApplyPendingRestoreIfAny(paths.DatabasePath, restoreLogger);

            if (restored is not null)
            {
                Log.Information("База данных восстановлена из бэкапа {FileName} до старта DI", restored);
            }

            var settingsStore = _host.Services.GetRequiredService<ISettingsStore>();
            AppThemes.Register();
            var savedKey = settingsStore.GetStringValue(ThemeManager.SettingsKeyName);
            ThemeManager.Apply(string.IsNullOrWhiteSpace(savedKey) ? AppThemes.LightKey : savedKey);

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

            _splash.Dispose();
            _splash = null;

            JumpListBuilder.Apply();

            if (_startupCommand is not null)
            {
                _host.Services.GetRequiredService<ShellViewModel>().HandleStartupCommand(_startupCommand);
            }

            PreloadViewModelsInBackground();
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
            catch
            {
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
            _singleInstanceGuard?.Dispose();
            _singleInstanceGuard = null;

            await Log.CloseAndFlushAsync();
            base.OnExit(e);
        }
    }

    private void PreloadViewModelsInBackground()
    {
        if (_host is null)
        {
            return;
        }

        var actionHolder = _host.Services.GetRequiredService<ActionHolder>();
        var tools = _host.Services.GetRequiredService<ToolsViewModel>();
        var updateManager = _host.Services.GetRequiredService<AppUpdateManager>();
        var appUpdate = _host.Services.GetRequiredService<AppUpdateViewModel>();
        var preloadSignal = _host.Services.GetRequiredService<PreloadCompletionSignal>();

        string? appUpdateNote = null;

        var dbSteps = new PreloadStep[]
        {
        };

        var ioSteps = new PreloadStep[]
        {
            new("Проверка инструментов", _ => SafeCheckToolsAsync(tools)),
            new("Проверка обновлений приложения", async ct =>
            {
                var note = await SafeCheckAppUpdateAsync(updateManager, appUpdate, ct);

                if (!string.IsNullOrWhiteSpace(note))
                {
                    appUpdateNote = note;
                }
            }),
        };

        var totalSteps = dbSteps.Length + ioSteps.Length;

        _ = Task.Run(async () =>
        {
            var cts = new CancellationTokenSource();
            var action = actionHolder.Register(
                "Предзагрузка данных",
                "Подготовка…",
                progressMax: totalSteps,
                ctx: cts,
                kind: ActionKind.Other);

            var failed = 0;

            try
            {
                using var scope = actionHolder.BeginScope(action);

                var ioTasks = ioSteps
                    .Select(step => Task.Run(async () =>
                    {
                        if (!await RunStepAsync(actionHolder, action, step, cts.Token))
                        {
                            Interlocked.Increment(ref failed);
                        }
                    }, cts.Token))
                    .ToArray();

                foreach (var step in dbSteps)
                {
                    if (!await RunStepAsync(actionHolder, action, step, cts.Token))
                    {
                        failed++;
                    }
                }

                await Task.WhenAll(ioTasks);

                var finalStatus = appUpdateNote
                    ?? (failed > 0 ? $"Готово, шагов с ошибками: {failed}" : "Готово");

                action.Finish(finalStatus);
                Log.Information("Фоновая предзагрузка данных завершена (ошибок шагов: {Failed})", failed);
            }
            catch (OperationCanceledException)
            {
                action.MarkCancelled("Отменено");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Фоновая предзагрузка данных прервана");
                action.Fail("Ошибка: " + ex.Message, ex);
            }
            finally
            {
                preloadSignal.Complete();
                cts.Dispose();
            }
        });
    }

    private static async Task<bool> RunStepAsync(
        ActionHolder actionHolder,
        ActionHolder.RunningAction parent,
        PreloadStep step,
        CancellationToken token)
    {
        var stepCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var child = actionHolder.Register(
            step.Status,
            "Выполняется…",
            progressMax: 1,
            ctx: stepCts,
            kind: ActionKind.Other,
            parent: parent);

        try
        {
            using var scope = actionHolder.BeginScope(child);
            await step.Action(stepCts.Token);
            child.SetProgress(1);
            child.Finish("Готово");
            return true;
        }
        catch (OperationCanceledException)
        {
            child.MarkCancelled("Отменено");
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Шаг предзагрузки «{Step}» не удался", step.Status);
            child.Fail("Ошибка: " + ex.Message, ex);
            return false;
        }
        finally
        {
            actionHolder.ProgressPlus(parent.Id);
        }
    }

    private sealed record PreloadStep(string Status, Func<CancellationToken, Task> Action);

    private static async Task SafeCheckToolsAsync(ToolsViewModel tools)
    {
        try
        {
            await tools.EnsureCheckedAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Фоновая проверка инструментов не удалась");
        }
    }

    private static async Task<string?> SafeCheckAppUpdateAsync(AppUpdateManager updateManager, AppUpdateViewModel viewModel, CancellationToken token)
    {
        try
        {
            var update = await updateManager.CheckForUpdateAsync(token);
            viewModel.SetPendingUpdate(update);

            if (update is null)
            {
                return null;
            }

            Log.Information("Доступно обновление приложения: {Version}", update.Version);
            return $"Доступно обновление приложения: {update.Version}";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Фоновая проверка обновлений приложения не удалась");
            return null;
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

    private static void ConfigureServices(
        IServiceCollection services,
        Paths paths,
        string settingsPath,
        WpfLogSink logSink,
        KeepShellLoggingOptions loggingOptions)
    {
        services.AddSingleton(paths);
        services.AddSingleton(logSink);
        services.AddSingleton(loggingOptions);

        services.AddSingleton<LiteDatabase>(_ => new(paths.DatabasePath));
        services.AddSingleton<DatabaseBackupService>(sp =>
            new(sp.GetRequiredService<LiteDatabase>(),
                paths.DatabasePath,
                sp.GetRequiredService<ILogger<DatabaseBackupService>>()));

        services.AddSingleton<PluginManager>();

        services.AddHttpClient("GitHub", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MediaOrcestrator/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient("Preview", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MediaOrcestrator/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<GitHubReleaseProvider>();
        services.AddSingleton<IReleaseProvider>(sp => sp.GetRequiredService<GitHubReleaseProvider>());
        services.AddSingleton<ToolVersionDetector>();
        services.AddSingleton<ToolInstaller>();
        services.AddSingleton<ToolManager>(sp =>
            new(Path.Combine(AppContext.BaseDirectory, "tools"),
                sp.GetRequiredService<IReleaseProvider>(),
                sp.GetRequiredService<ToolVersionDetector>(),
                sp.GetRequiredService<ToolInstaller>(),
                sp.GetRequiredService<ILogger<ToolManager>>()));
        services.AddSingleton<IToolPathProvider>(sp => sp.GetRequiredService<ToolManager>());
        services.AddSingleton<VideoTranscoder>();

        services.AddSingleton<TempManager>(sp =>
            new(paths.TempPath,
                sp.GetRequiredService<LiteDatabase>(),
                sp.GetRequiredService<ILogger<TempManager>>()));

        services.AddSingleton<StateManager>(sp =>
            new(paths.StatePath,
                sp.GetRequiredService<LiteDatabase>(),
                sp.GetRequiredService<ILogger<StateManager>>()));

        services.AddSingleton<ActionHolder>();
        services.AddSingleton<Orcestrator>();

        services.AddSingleton<CommentsRepository>();
        services.AddSingleton<CommentsService>();
        services.AddSingleton<MediaMergeService>();
        services.AddSingleton<SyncRetryRunner>();
        services.AddSingleton<BatchRenameService>();
        services.AddSingleton<CoverGenerator>();
        services.AddSingleton<BatchPreviewService>();
        services.AddSingleton<SyncPlanner>();
        services.AddSingleton<DatabaseMaintenanceService>(sp =>
        {
            var fullDbPath = Path.GetFullPath(paths.DatabasePath);
            var backupsPath = Path.Combine(Path.GetDirectoryName(fullDbPath)!, "backups");
            var logsPath = Path.Combine(AppContext.BaseDirectory, "logs");

            return new(sp.GetRequiredService<LiteDatabase>(),
                sp.GetRequiredService<DatabaseBackupService>(),
                paths.DatabasePath,
                paths.TempPath,
                logsPath,
                backupsPath,
                sp.GetRequiredService<ILogger<DatabaseMaintenanceService>>());
        });

        services.AddSingleton(sp =>
        {
            var updates = sp.GetRequiredService<UpdatePreferences>();

            return new ErrorReportOptions
            {
                IssueRepo = () => updates.Repo,
                LogFileGlobs = ["wpf-*.log"],
                SessionStartMarker = "MediaOrcestrator.Runner.Wpf",
            };
        });
        services.AddSingleton<ErrorReportService>();
        services.AddSingleton<AppUpdateManager>(sp =>
        {
            var updates = sp.GetRequiredService<UpdatePreferences>();

            return new(sp.GetRequiredService<IReleaseProvider>(),
                () => updates.Repo,
                "MediaOrcestrator-Wpf",
                sp.GetRequiredService<ILogger<AppUpdateManager>>());
        });

        PluginLoader.Configure(services, paths.PluginPath);

        services.AddSingleton<ISettingsStore>(_ => new SettingsStore(settingsPath));

        services.AddKeepShell();
        services.AddSingleton<IAuthUI, WpfAuthUI>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IAppLifetime, AppLifetime>();

        services.AddSingleton<ShellPreferences>();
        services.AddSingleton<UpdatePreferences>();
        services.AddSingleton<ThemeViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<TasksViewModel>();
        services.AddSingleton<ToolsViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<SystemViewModel>();
        services.AddSingleton<MaintenanceViewModel>();
        services.AddSingleton<DatabaseCleanupViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<AppUpdateViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddSingleton<PreloadCompletionSignal>();
        services.AddHostedService<AutoSyncHostedService>();
        services.AddHostedService<ToastNotificationService>();
    }
}
