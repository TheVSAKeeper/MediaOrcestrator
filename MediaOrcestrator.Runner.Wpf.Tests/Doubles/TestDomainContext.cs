using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using LiteDB;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Domain.Merging;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.MediaContextMenu;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.IO;
using System.Net.Http;

namespace MediaOrcestrator.Runner.Wpf.Tests.Doubles;

public sealed class TestDomainContext : IDisposable
{
    public TestDomainContext(IEnumerable<ISourceType>? sourceTypes = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "MediaOrcestrator.Wpf.Tests", Guid.NewGuid().ToString("N"));
        var pluginPath = Path.Combine(Root, "plugins");
        var tempPath = Path.Combine(Root, "temp");
        var statePath = Path.Combine(Root, "state");
        var dbPath = Path.Combine(Root, "MyData.db");
        var logsPath = Path.Combine(Root, "logs");
        var backupsPath = Path.Combine(Root, "backups");

        Directory.CreateDirectory(pluginPath);
        Directory.CreateDirectory(tempPath);
        Directory.CreateDirectory(statePath);
        Directory.CreateDirectory(logsPath);
        Directory.CreateDirectory(backupsPath);

        Paths = new(pluginPath, dbPath, tempPath, statePath);
        Database = new(":memory:");
        Settings = new();
        Dispatcher = new();
        RenderProbe = new();

        ActionHolder = new(NullLogger<ActionHolder>.Instance);
        PluginManager = new([.. sourceTypes ?? []], null!, NullLogger<PluginManager>.Instance);
        PluginManager.Init();

        TempManager = new(tempPath, Database, NullLogger<TempManager>.Instance);
        StateManager = new(statePath, Database, NullLogger<StateManager>.Instance);
        Orcestrator = new(PluginManager, Database, TempManager, StateManager, ActionHolder, NullLogger<Orcestrator>.Instance);

        MediaMergeService = new(Orcestrator, NullLogger<MediaMergeService>.Instance);
        SyncPlanner = new(NullLogger<SyncPlanner>.Instance);
        SyncRetryRunner = new(Orcestrator, NullLogger<SyncRetryRunner>.Instance);
        CoverGenerator = new(NullLogger<CoverGenerator>.Instance);
        CoverTemplateStore = new(NullLogger<CoverTemplateStore>.Instance);
        CommentsRepository = new(Database);
        CommentsService = new(CommentsRepository, NullLogger<CommentsService>.Instance);
        BatchRenameService = new(Orcestrator, ActionHolder, NullLogger<BatchRenameService>.Instance);
        PlaylistPublisher = new(ActionHolder, NullLogger<PlaylistPublisher>.Instance);
        DatabaseBackupService = new(Database, dbPath, NullLogger<DatabaseBackupService>.Instance);
        DatabaseMaintenanceService = new(
            Database,
            Orcestrator,
            DatabaseBackupService,
            dbPath,
            tempPath,
            logsPath,
            backupsPath,
            NullLogger<DatabaseMaintenanceService>.Instance);

        HttpClientFactory = Substitute.For<IHttpClientFactory>();
        BatchPreviewService = new(
            Orcestrator,
            TempManager,
            ActionHolder,
            NullLogger<BatchPreviewService>.Instance,
            HttpClientFactory,
            CoverGenerator);

        Dialogs = Substitute.For<IDialogService>();
        FilePicker = Substitute.For<IFilePicker>();
        Clipboard = Substitute.For<IClipboardService>();
        ShellLauncher = Substitute.For<IShellLauncher>();
        CoverEditorWindowService = Substitute.For<ICoverTemplateEditorWindowService>();
        Navigation = Substitute.For<INavigationService>();
        Lifetime = Substitute.For<IAppLifetime>();
        ReleaseProvider = Substitute.For<IReleaseProvider>();

        ActionContextFactory = new(
            Orcestrator,
            SyncRetryRunner,
            BatchRenameService,
            BatchPreviewService,
            CoverGenerator,
            MediaMergeService,
            ActionHolder,
            CommentsService,
            Dialogs,
            Navigation,
            NullLoggerFactory.Instance,
            NullLogger<MediaActionContextFactory>.Instance);

        ContextMenuBuilder = new([], NullLogger<MediaContextMenuBuilder>.Instance);
    }

    public string Root { get; }

    public Paths Paths { get; }

    public LiteDatabase Database { get; }

    public MemorySettings Settings { get; }

    public FakeUiDispatcher Dispatcher { get; }

    public NoOpPageRenderProbe RenderProbe { get; }

    public ActionHolder ActionHolder { get; }

    public PluginManager PluginManager { get; }

    public TempManager TempManager { get; }

    public StateManager StateManager { get; }

    public Orcestrator Orcestrator { get; }

    public MediaMergeService MediaMergeService { get; }

    public SyncPlanner SyncPlanner { get; }

    public SyncRetryRunner SyncRetryRunner { get; }

    public CoverGenerator CoverGenerator { get; }

    public CoverTemplateStore CoverTemplateStore { get; }

    public CommentsRepository CommentsRepository { get; }

    public CommentsService CommentsService { get; }

    public BatchRenameService BatchRenameService { get; }

    public BatchPreviewService BatchPreviewService { get; }

    public PlaylistPublisher PlaylistPublisher { get; }

    public DatabaseBackupService DatabaseBackupService { get; }

    public DatabaseMaintenanceService DatabaseMaintenanceService { get; }

    public IHttpClientFactory HttpClientFactory { get; }

    public IDialogService Dialogs { get; }

    public IFilePicker FilePicker { get; }

    public IClipboardService Clipboard { get; }

    public IShellLauncher ShellLauncher { get; }

    public ICoverTemplateEditorWindowService CoverEditorWindowService { get; }

    public INavigationService Navigation { get; }

    public IAppLifetime Lifetime { get; }

    public IReleaseProvider ReleaseProvider { get; }

    public MediaActionContextFactory ActionContextFactory { get; }

    public MediaContextMenuBuilder ContextMenuBuilder { get; }

    public void Dispose()
    {
        DatabaseBackupService.Dispose();
        CoverGenerator.Dispose();
        Database.Dispose();

        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
        catch
        {
        }
    }
}
