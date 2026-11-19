using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class MediaGridRetryTests
{
    private TestDomainContext _ctx = null!;
    private MediaViewModel _vm = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new();
        _vm = CreateViewModel(_ctx.Dispatcher);
    }

    private MediaViewModel CreateViewModel(IUiDispatcher dispatcher) =>
        new(
            _ctx.Orcestrator,
            _ctx.ActionHolder,
            _ctx.Settings,
            _ctx.ActionContextFactory,
            _ctx.ContextMenuBuilder,
            NullLoggerFactory.Instance,
            NullLogger<MediaViewModel>.Instance,
            _ctx.ShellLauncher,
            _ctx.Clipboard,
            dispatcher,
            _ctx.RenderProbe);

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public void ClearOnLoadError_ПослеУспешнойЗагрузки_СбрасываетСтрокиСчётчикиИВыделение()
    {
        _vm.Grid.ApplyDomainData(
        [
            new Media { Id = "m1", Title = "Первое" },
            new Media { Id = "m2", Title = "Второе" },
        ]);
        _vm.Grid.ApplySelectionChange([], [_vm.Grid.Rows[0]]);
        _ctx.Dispatcher.FireAllRunningTimers();

        Assert.That(_vm.Grid.TotalCount, Is.EqualTo(2));

        _vm.Grid.LoadError = "Сбой чтения";
        _vm.Grid.ClearOnLoadError();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.Grid.HasLoadError, Is.True);
            Assert.That(_vm.Grid.ShowEmptyState, Is.False);
            Assert.That(_vm.Grid.Rows, Is.Empty);
            Assert.That(_vm.Grid.TotalCount, Is.EqualTo(0));
            Assert.That(_vm.Grid.VisibleCount, Is.EqualTo(0));
            Assert.That(_vm.Grid.SelectedCount, Is.EqualTo(0));
            Assert.That(_vm.Grid.Focused, Is.Null);
            Assert.That(_vm.Grid.IsDatabaseEmpty, Is.True);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ReloadAsync_ПриОшибкеЧтенияБД_ВыставляетОшибкуИОчищаетУстаревшиеСтроки()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var vm = CreateViewModel(new StaTestDispatcher(dispatcher, _ctx.Dispatcher));
        _ctx.Database.GetCollection<Media>("medias").Insert(new Media { Id = "m1", Title = "Видео" });

        PumpTask(dispatcher, vm.ReloadAsync());
        Assert.That(vm.Grid.TotalCount, Is.EqualTo(1));

        _ctx.Database.Dispose();
        PumpTask(dispatcher, vm.ReloadAsync());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.Grid.HasLoadError, Is.True);
            Assert.That(vm.Grid.LoadError, Is.Not.Null.And.Not.Empty);
            Assert.That(vm.Grid.TotalCount, Is.EqualTo(0));
            Assert.That(vm.Grid.VisibleCount, Is.EqualTo(0));
            Assert.That(vm.Grid.Rows, Is.Empty);
        }
    }

    private static void PumpTask(Dispatcher dispatcher, Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(
                _ => dispatcher.BeginInvoke(() => frame.Continue = false),
                TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }

        task.GetAwaiter().GetResult();
    }

    private sealed class StaTestDispatcher(Dispatcher dispatcher, IUiDispatcher timerFallback) : IUiDispatcher
    {
        public bool HasAccess => dispatcher.CheckAccess();

        public void Invoke(Action action)
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Invoke(action);
            }
        }

        public IUiTimer CreateTimer(TimeSpan interval, Action tick) =>
            timerFallback.CreateTimer(interval, tick);
    }

    [Test]
    public async Task RetryLoadCommand_ПриВызове_ЗапускаетПерезагрузкуЧерезДействие()
    {
        var calls = 0;
        _vm.Grid.RetryLoadAction = () =>
        {
            calls++;
            return Task.CompletedTask;
        };

        await _vm.Grid.RetryLoadCommand.ExecuteAsync(null);

        Assert.That(calls, Is.EqualTo(1));
    }
}
