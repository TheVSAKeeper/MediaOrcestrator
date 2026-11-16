using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Bootstrap;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.IO;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed partial class ShellViewModelTests
{
    private TestDomainContext _ctx = null!;
    private ServiceProvider _provider = null!;
    private ActionHolder _actionHolder = null!;
    private ShellViewModel _shell = null!;

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
        services.AddSingleton<IUiDispatcher>(_ctx.Dispatcher);
        services.AddSingleton<IPageRenderProbe>(_ctx.RenderProbe);
        services.AddSingleton(_ctx.Dialogs);

        _provider = services.BuildServiceProvider();
        _actionHolder = _provider.GetRequiredService<ActionHolder>();
        _shell = _provider.GetRequiredService<ShellViewModel>();
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
        _ctx.Dispose();
    }

    [Test]
    public void TryNavigate_НаНастройкиИДочернийРазделОпераций_ОбновляетВыборБезПерсистаГруппы()
    {
        var navToSettings = _shell.TryNavigate("settings");
        var selectedAfterSettings = _shell.IsSettingsSelected;

        var navToMerge = _shell.TryNavigate("Слияние дубликатов");
        var operationsGroup = _shell.Sections.First(s => s.Key == "operations");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(navToSettings, Is.True);
            Assert.That(selectedAfterSettings, Is.True);
            Assert.That(navToMerge, Is.True);
            Assert.That(_shell.Selected?.Key, Is.EqualTo("merge"));
            Assert.That(operationsGroup.IsExpanded, Is.True);
            Assert.That(_ctx.Settings.GetStringValue("wpf.shell.nav_operations_expanded"), Is.Null);
        }
    }

    [Test]
    public void HandleStartupCommand_ДляНавигацииИДровераЗадач_ПереключаетСостояние()
    {
        _shell.HandleStartupCommand(StartupCommandLine.ToCommand(["--nav=transfers"])!);
        var afterTransfersKey = _shell.Selected?.Key;

        _shell.HandleStartupCommand(StartupCommandLine.ToCommand(["--nav=settings"])!);
        var afterSettings = _shell.IsSettingsSelected;

        _shell.HandleStartupCommand(StartupCommandLine.ToCommand(["--drawer=tasks"])!);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterTransfersKey, Is.EqualTo("transfers"));
            Assert.That(afterSettings, Is.True);
            Assert.That(_shell.IsTasksDrawerOpen, Is.True);
        }
    }

    [Test]
    public void HandleEscapeCommand_СоблюдаетКаскадПриоритетовМодалДроверОтменаСтраница()
    {
        var page = new FakeStatusAndEscapePage();
        _shell.Sections.Add(new("Тест", MahApps.Metro.IconPacks.PackIconLucideKind.Bug, page, key: "test-escape"));
        _shell.NavigateByKey("test-escape");

        _shell.IsTasksDrawerOpen = true;
        page.IsBusy = true;

        _ = _shell.Modal.ShowAsync(new DummyDialogViewModel());
        _shell.HandleEscapeCommand.Execute(null);
        var drawerStillOpenWithModal = _shell.IsTasksDrawerOpen;

        _shell.Modal.RequestCancel();
        _shell.HandleEscapeCommand.Execute(null);
        var drawerAfterFirstEscape = _shell.IsTasksDrawerOpen;

        _shell.HandleEscapeCommand.Execute(null);
        var cancelExecutions = page.CancelExecutions;
        page.IsBusy = false;

        _shell.HandleEscapeCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(drawerStillOpenWithModal, Is.True);
            Assert.That(drawerAfterFirstEscape, Is.False);
            Assert.That(cancelExecutions, Is.EqualTo(1));
            Assert.That(page.EscapeHandledCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void RequestClose_ПриАктивныхЗадачах_СпрашиваетПодтверждениеССклонением()
    {
        var noTasksClose = _shell.RequestClose();

        using var cts1 = new CancellationTokenSource();
        var action1 = _actionHolder.Register("Первая задача", "Выполняется", 0, cts1);
        _ctx.Dialogs.ConfirmWarning(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var oneTaskClose = _shell.RequestClose();
        _ctx.Dialogs.Received(1).ConfirmWarning(
            "Закрыть приложение?",
            Arg.Is<string>(m => m.Contains("одна задача")));

        using var cts2 = new CancellationTokenSource();
        var action2 = _actionHolder.Register("Вторая задача", "Выполняется", 0, cts2);
        _ctx.Dialogs.ConfirmWarning(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var twoTasksClose = _shell.RequestClose();
        _ctx.Dialogs.Received(1).ConfirmWarning(
            "Закрыть приложение?",
            Arg.Is<string>(m => m.Contains("задач: 2")));

        action1.Finish("Готово");
        action2.Finish("Готово");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(noTasksClose, Is.True);
            Assert.That(oneTaskClose, Is.False);
            Assert.That(twoTasksClose, Is.True);
        }
    }

    [Test]
    public void ActionHolder_ПриДобавленииИЗавершенииЗадач_ОбновляетStatusText()
    {
        var initialStatus = _shell.StatusText;

        using var cts1 = new CancellationTokenSource();
        var action1 = _actionHolder.Register("Синхронизация", "Выполняется", 0, cts1);
        var singleTaskStatus = _shell.StatusText;

        using var cts2 = new CancellationTokenSource();
        var action2 = _actionHolder.Register("Загрузка", "Выполняется", 0, cts2);
        var twoTasksStatus = _shell.StatusText;

        action1.Finish("Готово");
        action2.Finish("Готово");
        var afterFinishStatus = _shell.StatusText;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(initialStatus, Is.EqualTo("Готов"));
            Assert.That(singleTaskStatus, Is.EqualTo("Синхронизация"));
            Assert.That(twoTasksStatus, Is.EqualTo("Работает 2 задач(и)"));
            Assert.That(afterFinishStatus, Is.EqualTo("Готов"));
        }
    }

    private sealed class DummyDialogViewModel : ObservableObject, IDialogViewModel
    {
        public event EventHandler<bool>? RequestClose
        {
            add { }
            remove { }
        }
    }

    private sealed partial class FakeStatusAndEscapePage : ObservableObject, IPageStatus, IPageEscapeHandler
    {
        [ObservableProperty]
        private bool _isBusy;

        public FakeStatusAndEscapePage()
        {
            CancelCommand = new RelayCommand(() => CancelExecutions++);
        }

        public string? StatusCaption => null;

        public bool IsIndeterminate => true;

        public double ProgressValue => 0;

        public double ProgressMax => 100;

        public ICommand? CancelCommand { get; }

        public int CancelExecutions { get; private set; }

        public bool CanHandleEscape => true;

        public int EscapeHandledCount { get; private set; }

        public bool TryHandleEscape()
        {
            EscapeHandledCount++;
            return true;
        }
    }
}
