using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class MediaInspectorAutoCollapseTests
{
    private TestDomainContext _ctx = null!;
    private MediaViewModel _vm = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new();
        _vm = CreateViewModel();
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public void UpdateViewportWidth_НижеПорогаБезВыделения_СворачиваетБезПерезаписиНастроекИВосстанавливаетНаШироком()
    {
        _vm.UpdateViewportWidth(1280);
        var wideInitial = _vm.IsInspectorCollapsed;

        _vm.UpdateViewportWidth(960);
        var collapsedOnNarrow = _vm.IsInspectorCollapsed;
        var persistedWhileAuto = _ctx.Settings.GetStringValue("wpf.media.inspector.collapsed");

        _vm.UpdateViewportWidth(1100);
        var restoredOnWide = _vm.IsInspectorCollapsed;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wideInitial, Is.False);
            Assert.That(collapsedOnNarrow, Is.True);
            Assert.That(persistedWhileAuto, Is.Null);
            Assert.That(restoredOnWide, Is.False);
        }
    }

    [Test]
    public void UpdateViewportWidth_ПриВыделенииМедиаНаУзкомОкне_АвтоРаскрываетИнспектор()
    {
        _vm.Grid.ApplyDomainData([new Media { Id = "m1", Title = "Ролик" }]);
        _vm.UpdateViewportWidth(900);
        var collapsedBeforeSelect = _vm.IsInspectorCollapsed;

        _vm.Grid.ApplySelectionChange([], [_vm.Grid.Rows[0]]);
        _ctx.Dispatcher.FireAllRunningTimers();
        var expandedOnSelection = _vm.IsInspectorCollapsed;

        _vm.Grid.ApplySelectionChange([_vm.Grid.Rows[0]], []);
        _ctx.Dispatcher.FireAllRunningTimers();
        var collapsedAfterDeselect = _vm.IsInspectorCollapsed;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collapsedBeforeSelect, Is.True);
            Assert.That(expandedOnSelection, Is.False);
            Assert.That(collapsedAfterDeselect, Is.True);
            Assert.That(_ctx.Settings.GetStringValue("wpf.media.inspector.collapsed"), Is.Null);
        }
    }

    [Test]
    public void ToggleInspectorCollapsed_НаУзкомОкне_СохраняетРучноеПереопределениеДоРасширенияОкна()
    {
        _vm.UpdateViewportWidth(960);
        _vm.ToggleInspectorCollapsedCommand.Execute(null);
        var expandedManually = _vm.IsInspectorCollapsed;

        _vm.UpdateViewportWidth(880);
        var stayedExpandedWhileNarrow = _vm.IsInspectorCollapsed;

        _vm.UpdateViewportWidth(1150);
        _vm.UpdateViewportWidth(960);
        var autoCollapsedAfterWideToNarrow = _vm.IsInspectorCollapsed;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(expandedManually, Is.False);
            Assert.That(stayedExpandedWhileNarrow, Is.False);
            Assert.That(autoCollapsedAfterWideToNarrow, Is.True);
        }
    }

    [Test]
    public void UpdateViewportWidth_ЕслиПользовательСвернулИнспекторСам_НеРаскрываетПриРасширении()
    {
        _ctx.Settings.SetValue("wpf.media.inspector.collapsed", "true");
        var vm = CreateViewModel();

        vm.UpdateViewportWidth(900);
        vm.UpdateViewportWidth(1280);

        Assert.That(vm.IsInspectorCollapsed, Is.True);
    }

    private MediaViewModel CreateViewModel()
    {
        return new(
            _ctx.Orcestrator,
            _ctx.ActionHolder,
            _ctx.Settings,
            _ctx.ActionContextFactory,
            _ctx.ContextMenuBuilder,
            NullLoggerFactory.Instance,
            NullLogger<MediaViewModel>.Instance,
            _ctx.ShellLauncher,
            _ctx.Clipboard,
            _ctx.Dispatcher,
            _ctx.RenderProbe);
    }
}
