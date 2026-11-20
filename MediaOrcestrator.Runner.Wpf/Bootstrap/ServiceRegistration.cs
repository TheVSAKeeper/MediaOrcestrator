using KeepShell.Diagnostics;
using KeepShell.Mcp;
using LiteDB;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Domain.Merging;
using MediaOrcestrator.Domain.Migration;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Mcp;
using MediaOrcestrator.Runner.Wpf.MediaContextMenu;
using MediaOrcestrator.Runner.Wpf.MediaContextMenu.Actions;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.Services.Diagnostics;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using MediaOrcestrator.Runner.Wpf.Views;
using MediaOrcestrator.Runner.Wpf.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

internal static class ServiceRegistration
{
    public static void AddMediaOrcestrator(
        IServiceCollection services,
        Paths paths,
        string settingsPath,
        WpfLogSink logSink,
        KeepShellLoggingOptions loggingOptions,
        ILogger? pluginLogger = null)
    {
        services.AddSingleton<ISettingsStore>(_ => new SettingsStore(settingsPath));
        RegisterCoreServices(services, paths, logSink, loggingOptions, pluginLogger);
    }

    public static void AddMediaOrcestrator(
        IServiceCollection services,
        Paths paths,
        ISettingsStore settingsStore,
        WpfLogSink logSink,
        KeepShellLoggingOptions loggingOptions)
    {
        services.AddSingleton(settingsStore);
        RegisterCoreServices(services, paths, logSink, loggingOptions);
    }

    public static ServiceProvider BuildServiceProvider(
        Paths paths,
        ISettingsStore settingsStore,
        WpfLogSink logSink,
        KeepShellLoggingOptions loggingOptions)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        AddMediaOrcestrator(services, paths, settingsStore, logSink, loggingOptions);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static void RegisterCoreServices(
        IServiceCollection services,
        Paths paths,
        WpfLogSink logSink,
        KeepShellLoggingOptions loggingOptions,
        ILogger? pluginLogger = null)
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
        services.AddSingleton<PlaylistPublisher>();

        services.AddSingleton<CommentsRepository>();
        services.AddSingleton<CommentsService>();
        services.AddSingleton<MediaMergeService>();
        services.AddSingleton<SyncRetryRunner>();
        services.AddSingleton<BatchRenameService>();
        services.AddSingleton<CoverGenerator>();
        services.AddSingleton<CoverTemplateStore>();
        services.AddSingleton<ICoverTemplateEditorWindowService, CoverTemplateEditorWindowService>();
        services.AddSingleton<BatchPreviewService>();
        services.AddSingleton<SyncPlanner>();
        services.AddSingleton<DatabaseMaintenanceService>(sp =>
        {
            var fullDbPath = Path.GetFullPath(paths.DatabasePath);
            var backupsPath = Path.Combine(Path.GetDirectoryName(fullDbPath)!, "backups");

            return new(sp.GetRequiredService<LiteDatabase>(),
                sp.GetRequiredService<Orcestrator>(),
                sp.GetRequiredService<DatabaseBackupService>(),
                paths.DatabasePath,
                paths.TempPath,
                loggingOptions.LogsDirectory,
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

        PluginLoader.Configure(services, paths.PluginPath, pluginLogger);

        var fullDbPath = Path.GetFullPath(paths.DatabasePath);
        var dataDirectory = Path.GetDirectoryName(fullDbPath)!;
        var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);

        services.AddKeepShell();
        services.AddKeepShellToasts();
        services.AddKeepShellDiagnostics(new DiagnosticsOptions
        {
            AppName = "MediaOrcestrator",
            AppVersion = App.ResolveAppVersion(),
            DataDirectory = dataDirectory,
            PortableStorage = dataDirectory.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase),
        });
        services.AddSingleton<IDiagnosticsSecretSource, MediaOrcestratorSecretSource>();
        services.AddSingleton<IDiagnosticsBundleSource, MediaOrcestratorBundleSource>();
        services.AddSingleton<IDiagnosticsCard, DatabaseDiagnosticsCard>();
        services.AddSingleton<IDiagnosticsCard, SourcesPluginsDiagnosticsCard>();
        services.AddSingleton<IDiagnosticsCard, ToolsDiagnosticsCard>();
        services.AddSingleton<IDiagnosticsCard, StoragePathsDiagnosticsCard>();
        services.AddSingleton<MediaOrcestratorAutomation>();
        services.AddKeepShellMcp<ShellViewModel>(new McpServerOptions
        {
            ServerName = "mediaorcestrator",
            AppName = "MediaOrcestrator",
            AppVersion = App.ResolveAppVersion(),
            DefaultPort = 7655,
            ShotsDirectory = Path.Combine(AppContext.BaseDirectory, "shots"),
            ToolTypes = [typeof(MediaOrcestratorTools)],
            ConfigureTools = static (provider, tools) => tools.AddSingleton(provider.GetRequiredService<MediaOrcestratorAutomation>()),
        });
        services.AddSingleton<WpfAuthUI>();
        services.AddSingleton<IAuthUI>(provider => provider.GetRequiredService<WpfAuthUI>());
        services.AddSingleton<Func<SourceAuthenticationPromptContext, CancellationToken, Action<string>, IAuthUI>>(
            provider => provider.GetRequiredService<WpfAuthUI>().CreateSession);
        services.AddSingleton<SourceAuthenticationService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IAppLifetime, AppLifetime>();
        services.AddSingleton<IPageRenderProbe, WpfPageRenderProbe>();
        services.AddSingleton<MediaActionContextFactory>();
        services.AddSingleton<MediaContextMenuBuilder>();
        services.AddSingleton<IMediaMenuAction, MetadataAction>();
        services.AddSingleton<IMediaMenuAction, SyncAction>();
        services.AddSingleton<IMediaMenuAction, SkipAction>();
        services.AddSingleton<IMediaMenuAction, SkipPlanAction>();
        services.AddSingleton<IMediaMenuAction, EditAction>();
        services.AddSingleton<IMediaMenuAction, CopyDetailsAction>();
        services.AddSingleton<IMediaMenuAction, OpenExternalAction>();
        services.AddSingleton<IMediaMenuAction, DeleteAction>();
        services.AddSingleton<IMediaMenuAction, RemoveFromDatabaseAction>();
        services.AddSingleton<IMediaMenuAction, ConvertAction>();

        services.AddSingleton<ShellPreferences>();
        services.AddSingleton<UpdatePreferences>();
        services.AddSingleton<ThemeViewModel>();
        services.AddSingleton<MediaViewModel>();
        services.AddSingleton<AuditViewModel>();
        services.AddSingleton<TransfersViewModel>();
        services.AddSingleton<PublishViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<TasksViewModel>();
        services.AddSingleton<ToolsViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<CleanupAndBackupsViewModel>();
        services.AddSingleton<MaintenanceHubViewModel>();
        services.AddSingleton<SourcesRelationsViewModel>();
        services.AddSingleton<SourcesAuthenticationViewModel>();
        services.AddSingleton<CommentsViewModel>();
        services.AddSingleton<BatchRenameSubViewModel>();
        services.AddSingleton<BatchPreviewSubViewModel>();
        services.AddSingleton<MergeDuplicatesSubViewModel>();
        services.AddSingleton<ResolveConflictsSubViewModel>();
        services.AddSingleton<OperationsViewModel>();
        services.AddSingleton<MaintenanceViewModel>();
        services.AddSingleton<DatabaseCleanupViewModel>();
        services.AddSingleton<ChronologyViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<AppUpdateViewModel>();
        services.AddSingleton<IImportSettingsWriter, TomlImportSettingsWriter>();
        services.AddSingleton<IWinFormsProcessDetector, WinFormsProcessDetector>();
        services.AddSingleton<IShortcutCreator, WindowsShortcutCreator>();
        services.AddSingleton(sp => new WinFormsInstallationInspector(sp.GetRequiredService<IWinFormsProcessDetector>()));
        services.AddSingleton<WinFormsSnapshotStager>();
        services.AddSingleton<WinFormsImportService>();
        services.AddSingleton<IWinFormsImportLauncher, WinFormsImportLauncher>();
        services.AddTransient<WinFormsImportDialogViewModel>();
        services.AddTransient<WinFormsImportDialogView>();
        services.AddTransient<SourceSettingsViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddSingleton<PreloadCompletionSignal>();
        services.AddHostedService<AutoSyncHostedService>();
        services.AddHostedService<ToastNotificationService>();
        services.AddHostedService<ChildProcessSampler>();
    }
}
