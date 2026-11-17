using KeepShell.Bootstrap;
using LiteDB;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Migration;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class WinFormsImportDialogViewModelTests
{
    private TestDomainContext _ctx = null!;
    private string _winFormsDir = null!;
    private string _currentWpfDir = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new();
        _winFormsDir = Path.Combine(_ctx.Root, "winforms-0059");
        _currentWpfDir = Path.Combine(_ctx.Root, "current-wpf");
        Directory.CreateDirectory(_winFormsDir);
        Directory.CreateDirectory(_currentWpfDir);

        File.WriteAllText(Path.Combine(_currentWpfDir, "MediaOrcestrator.Runner.Wpf.exe"), "wpf");
        File.WriteAllText(Path.Combine(_currentWpfDir, "MediaOrcestrator.Runner.Wpf.dll"), "wpf-dll");
        CreateSampleWinFormsInstallation(_winFormsDir);
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task SourceFolderPath_ПриУказанииВалиднойПапки_ЗагружаетСводкуИСчетчики()
    {
        var detector = Substitute.For<IWinFormsProcessDetector>();
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>()).Returns(WinFormsProcessStatus.Free);
        var vm = CreateViewModel(detector);

        vm.SourceFolderPath = _winFormsDir;
        await vm.ActiveInspectionTask!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.IsSourceValid, Is.True);
            Assert.That(vm.InspectionErrorMessage, Is.Null);
            Assert.That(vm.DetectedVersionText, Is.EqualTo("0.0.5.9"));
            Assert.That(vm.HasSummary, Is.True);
            Assert.That(vm.SourcesCount, Is.EqualTo(1));
            Assert.That(vm.MediasCount, Is.EqualTo(1));
            Assert.That(vm.CoverTemplatesCount, Is.EqualTo(1));
            Assert.That(vm.CanStartImport, Is.False);
        }

        vm.TargetFolderPath = Path.Combine(_ctx.Root, "new-wpf");
        Assert.That(vm.CanStartImport, Is.True);
    }

    [Test]
    public async Task SourceFolderPath_КогдаWinFormsЗапущен_ЖдетЗакрытияИПослеОсвобожденияПеречитываетСводку()
    {
        var detector = Substitute.For<IWinFormsProcessDetector>();
        var busyStatus = new WinFormsProcessStatus(true, true, [4242], true, false);
        var isBusy = true;

        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => isBusy ? busyStatus : WinFormsProcessStatus.Free);

        var waitGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        detector.WaitForCloseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(waitGate.Task);

        var vm = CreateViewModel(detector);
        vm.TargetFolderPath = Path.Combine(_ctx.Root, "new-wpf");
        vm.SourceFolderPath = _winFormsDir;
        await vm.ActiveInspectionTask!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.IsSourceValid, Is.True);
            Assert.That(vm.IsSourceRunning, Is.True);
            Assert.That(vm.IsWaitingForExit, Is.True);
            Assert.That(vm.RunningPidsText, Is.EqualTo("PID 4242"));
            Assert.That(vm.HasSummary, Is.False);
            Assert.That(vm.CanStartImport, Is.False);
        }

        isBusy = false;
        waitGate.SetResult();
        await vm.ActiveWaitTask!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.IsWaitingForExit, Is.False);
            Assert.That(vm.IsSourceRunning, Is.False);
            Assert.That(vm.HasSummary, Is.True);
            Assert.That(vm.SourcesCount, Is.EqualTo(1));
            Assert.That(vm.CanStartImport, Is.True);
        }
    }

    [Test]
    public async Task CancelWaiting_ПриОжиданииЗакрытия_ОстанавливаетОжиданиеИПоказываетРучнойПовтор()
    {
        var detector = Substitute.For<IWinFormsProcessDetector>();
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new WinFormsProcessStatus(true, false, [777], false, false));

        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        detector.WaitForCloseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var token = call.Arg<CancellationToken>();
                return Task.Delay(TimeSpan.FromSeconds(10), token).ContinueWith(t =>
                {
                    if (t.IsCanceled)
                    {
                        cancelled.TrySetResult();
                        throw new OperationCanceledException(token);
                    }
                }, TaskScheduler.Default);
            });

        var vm = CreateViewModel(detector);
        vm.SourceFolderPath = _winFormsDir;
        await vm.ActiveInspectionTask!;

        Assert.That(vm.IsWaitingForExit, Is.True);

        vm.CancelWaitingCommand.Execute(null);
        await cancelled.Task;
        await vm.ActiveWaitTask!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.IsWaitingForExit, Is.False);
            Assert.That(vm.IsSourceRunning, Is.True);
            Assert.That(vm.ShowPausedRunningNotice, Is.True);
        }
    }

    [Test]
    public async Task StartImport_ОтдельныйЭкземпляр_СоздаетФайлыИНастройкиИЯрлык()
    {
        var detector = Substitute.For<IWinFormsProcessDetector>();
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>()).Returns(WinFormsProcessStatus.Free);
        var shortcutCreator = Substitute.For<IShortcutCreator>();

        var vm = CreateViewModel(detector, shortcutCreator);
        var targetDir = Path.Combine(_ctx.Root, "wpf-copy");
        vm.SourceFolderPath = _winFormsDir;
        await vm.ActiveInspectionTask!;
        vm.TargetFolderPath = targetDir;

        await vm.StartImportCommand.ExecuteAsync(null);

        var generatedSettingsPath = Path.Combine(targetDir, "settings.toml");
        var settingsTable = TomlSettingsFile.Read(generatedSettingsPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.IsCompleted, Is.True);
            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(vm.SeparateResult, Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(targetDir, "MyData.db")), Is.True);
            Assert.That(File.Exists(generatedSettingsPath), Is.True);
            Assert.That(TomlSettingsFile.GetString(settingsTable, "wpf.sync.auto_enabled"), Is.EqualTo("false"));
            Assert.That(TomlSettingsFile.GetString(settingsTable, "wpf.import.completed"), Is.EqualTo("true"));
            shortcutCreator.Received(1).CreateShortcut(
                Arg.Any<string>(),
                Path.Combine(targetDir, "MediaOrcestrator.Runner.Wpf.exe"),
                targetDir,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                0);
        }
    }

    [Test]
    public async Task StartImport_ЗаменитьТекущий_ГотовитОтложенныйИмпортИПерезапускаетСApplyImport()
    {
        var detector = Substitute.For<IWinFormsProcessDetector>();
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>()).Returns(WinFormsProcessStatus.Free);
        _ctx.ShellLauncher.Start(Arg.Any<string>(), Arg.Any<string[]>()).Returns(true);

        var vm = CreateViewModel(detector);
        var closedWith = new List<bool>();
        vm.RequestClose += (_, result) => closedWith.Add(result);
        vm.SourceFolderPath = _winFormsDir;
        await vm.ActiveInspectionTask!;
        vm.IsReplaceCurrentMode = true;
        vm.EnableAutoSync = true;

        await vm.StartImportCommand.ExecuteAsync(null);

        var pendingPath = Path.Combine(_currentWpfDir, WinFormsImportService.PendingManifestFileName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(File.Exists(pendingPath), Is.True);
            Assert.That(closedWith, Is.EqualTo(new[] { true }));
            _ctx.ShellLauncher.Received(1).Start(
                Path.Combine(_currentWpfDir, "MediaOrcestrator.Runner.Wpf.exe"),
                Arg.Is<string[]>(args => args.Length == 1 && args[0].StartsWith("--apply-import=", StringComparison.Ordinal)));
            _ctx.Lifetime.Received(1).Shutdown();
        }
    }

    [Test]
    public async Task StartImport_ПриОшибкеНепустойЦелевойПапки_ОстаетсяОткрытымИПоказываетОшибку()
    {
        var detector = Substitute.For<IWinFormsProcessDetector>();
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>()).Returns(WinFormsProcessStatus.Free);

        var nonEmptyTarget = Path.Combine(_ctx.Root, "occupied");
        Directory.CreateDirectory(nonEmptyTarget);
        File.WriteAllText(Path.Combine(nonEmptyTarget, "existing.txt"), "keep");

        var vm = CreateViewModel(detector);
        var closeCount = 0;
        vm.RequestClose += (_, _) => closeCount++;
        vm.SourceFolderPath = _winFormsDir;
        await vm.ActiveInspectionTask!;
        vm.TargetFolderPath = nonEmptyTarget;

        await vm.StartImportCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.IsCompleted, Is.False);
            Assert.That(vm.ErrorMessage, Is.Not.Null.And.Not.Empty);
            Assert.That(closeCount, Is.EqualTo(0));
            _ctx.Lifetime.DidNotReceive().Shutdown();
        }
    }

    [Test]
    public async Task CancelImport_ВоВремяВыполнения_ОтменяетИНеЗакрываетДиалог()
    {
        var detector = Substitute.For<IWinFormsProcessDetector>();
        var callCount = 0;
        var enteredImport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var gate = new ManualResetEventSlim(false);

        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>()).Returns(_ =>
        {
            var current = Interlocked.Increment(ref callCount);
            if (current >= 3)
            {
                enteredImport.TrySetResult();
                gate.Wait(TimeSpan.FromSeconds(5));
            }

            return WinFormsProcessStatus.Free;
        });

        var vm = CreateViewModel(detector);
        var closeCount = 0;
        vm.RequestClose += (_, _) => closeCount++;
        vm.SourceFolderPath = _winFormsDir;
        await vm.ActiveInspectionTask!;
        vm.TargetFolderPath = Path.Combine(_ctx.Root, "cancel-target");

        var importTask = vm.StartImportCommand.ExecuteAsync(null);
        await enteredImport.Task;
        vm.CancelImportCommand.Execute(null);
        gate.Set();
        await importTask;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.IsImporting, Is.False);
            Assert.That(vm.IsCompleted, Is.False);
            Assert.That(vm.ProgressStatusText, Is.EqualTo("Перенос отменён."));
            Assert.That(closeCount, Is.EqualTo(0));
            _ctx.Lifetime.DidNotReceive().Shutdown();
        }
    }

    [Test]
    public void DismissFirstRun_ВыставляетФлагИЗакрываетДиалогСFalse()
    {
        var detector = Substitute.For<IWinFormsProcessDetector>();
        var vm = CreateViewModel(detector);
        vm.Configure(isFirstRunPrompt: true);

        bool? closedResult = null;
        vm.RequestClose += (_, result) => closedResult = result;

        vm.DismissFirstRunCommand.Execute(null);
        ISettingsStore settingsStore = _ctx.Settings;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settingsStore.GetBool(FirstRunImportDetector.PromptDismissedKey), Is.True);
            Assert.That(closedResult, Is.False);
        }
    }

    [Test]
    public async Task CancelWaiting_при_одновременном_завершении_опроса_не_перезапускает_инспекцию()
    {
        var detector = Substitute.For<IWinFormsProcessDetector>();
        var inspectCalls = 0;
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>()).Returns(_ =>
        {
            Interlocked.Increment(ref inspectCalls);
            return new WinFormsProcessStatus(true, true, [111], true, false);
        });

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        detector.WaitForCloseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(gate.Task);

        var vm = CreateViewModel(detector);
        vm.SourceFolderPath = _winFormsDir;
        await vm.ActiveInspectionTask!;

        var callsBeforeCancel = Volatile.Read(ref inspectCalls);
        vm.CancelWaitingCommand.Execute(null);
        gate.SetResult();
        await vm.ActiveWaitTask!;

        Assert.That(Volatile.Read(ref inspectCalls), Is.EqualTo(callsBeforeCancel));
    }

    [Test]
    public async Task StartImport_ЗаменитьТекущий_ПередаётТекущиеПутиИзDIАНеНеприменённыйToml()
    {
        var activeDbPath = Path.Combine(_currentWpfDir, "active.db");
        var activeStateDir = Path.Combine(_currentWpfDir, "active_state");
        var unappliedDbPath = Path.Combine(_currentWpfDir, "future.db");
        var unappliedStateDir = Path.Combine(_currentWpfDir, "future_state");

        var tomlWriter = new TomlImportSettingsWriter();
        tomlWriter.WriteSettings(Path.Combine(_currentWpfDir, "settings.toml"), new Dictionary<string, string>
        {
            ["database_path"] = unappliedDbPath,
            ["state_path"] = unappliedStateDir,
        });

        var detector = Substitute.For<IWinFormsProcessDetector>();
        detector.CheckRunning(Arg.Any<string>(), Arg.Any<string>()).Returns(WinFormsProcessStatus.Free);
        _ctx.ShellLauncher.Start(Arg.Any<string>(), Arg.Any<string[]>()).Returns(true);

        var activePaths = new Paths(
            Path.Combine(_currentWpfDir, "ModuleBuilds"),
            activeDbPath,
            Path.Combine(_currentWpfDir, "temp"),
            activeStateDir);

        var vm = CreateViewModel(detector, currentPaths: activePaths);
        vm.SourceFolderPath = _winFormsDir;
        await vm.ActiveInspectionTask!;
        vm.IsReplaceCurrentMode = true;

        await vm.StartImportCommand.ExecuteAsync(null);

        var pendingJson = File.ReadAllText(Path.Combine(_currentWpfDir, WinFormsImportService.PendingManifestFileName));
        var manifest = System.Text.Json.JsonSerializer.Deserialize<WinFormsPendingImportManifest>(pendingJson);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest!.PreviousDatabasePath, Is.EqualTo(Path.GetFullPath(activeDbPath)));
            Assert.That(manifest.PreviousStatePath, Is.EqualTo(Path.GetFullPath(activeStateDir)));
        }
    }

    private WinFormsImportDialogViewModel CreateViewModel(
        IWinFormsProcessDetector detector,
        IShortcutCreator? shortcutCreator = null,
        Paths? currentPaths = null)
    {
        var inspector = new WinFormsInstallationInspector(detector, _ => new Version(0, 0, 5, 9));
        var stager = new WinFormsSnapshotStager(detector);
        var settingsWriter = new TomlImportSettingsWriter();
        var shortcuts = shortcutCreator ?? Substitute.For<IShortcutCreator>();
        var importService = new WinFormsImportService(
            inspector,
            stager,
            settingsWriter,
            shortcuts,
            NullLogger<WinFormsImportService>.Instance);
        var updates = new UpdatePreferences(_ctx.Settings);

        return new WinFormsImportDialogViewModel(
            inspector,
            detector,
            importService,
            _ctx.Settings,
            _ctx.FilePicker,
            _ctx.ShellLauncher,
            _ctx.Lifetime,
            updates,
            _ctx.Database,
            NullLogger<WinFormsImportDialogViewModel>.Instance,
            currentPaths)
        {
            CurrentAppBaseDirectory = _currentWpfDir,
            ProcessPollInterval = TimeSpan.FromMilliseconds(10),
        };
    }

    private static void CreateSampleWinFormsInstallation(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "MediaOrcestrator.Runner.dll"), "classic");
        File.WriteAllText(Path.Combine(dir, "settings.txt"), "database_path MyData.db\r\nstate_path state\r\ntemp_path temp\r\n");

        var stateDir = Path.Combine(dir, "state");
        var coversDir = Path.Combine(dir, "templates", "covers");
        Directory.CreateDirectory(stateDir);
        Directory.CreateDirectory(coversDir);
        File.WriteAllText(Path.Combine(coversDir, "sample.json"), "{\"Name\":\"Sample\"}");

        using var db = new LiteDatabase(Path.Combine(dir, "MyData.db"));
        var sources = db.GetCollection<Source>("sources");
        sources.Insert(new Source
        {
            Id = "src-1",
            TypeId = "Youtube",
            Settings = new Dictionary<string, string>
            {
                ["_system_name"] = "Канал 1",
            },
        });

        var medias = db.GetCollection<Media>("medias");
        medias.Insert(new Media
        {
            Id = "media-1",
            Title = "Ролик 1",
        });
    }
}
