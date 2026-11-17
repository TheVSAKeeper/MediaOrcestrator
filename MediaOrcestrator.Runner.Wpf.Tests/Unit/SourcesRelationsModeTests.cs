using KeepShell.Testing;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class SourcesRelationsModeTests
{
    private TestDomainContext _ctx = null!;
    private SourcesRelationsViewModel _vm = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
    }

    [SetUp]
    public void SetUp()
    {
        _ctx = new();
        _vm = new(
            _ctx.Orcestrator,
            _ctx.ActionHolder,
            new ServiceCollection().BuildServiceProvider(),
            _ctx.StateManager,
            _ctx.Settings,
            _ctx.Dialogs,
            _ctx.Dispatcher,
            NullLogger<SourcesRelationsViewModel>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public void IsGraphModeAndIsListMode_ЗаписьПереключаетРежимИИгнорируетFalse()
    {
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _vm.IsGraphMode = true;
        var modeAfterGraphTrue = _vm.ViewMode;
        var settingAfterGraph = _ctx.Settings.GetStringValue("wpf.sources.viewMode");

        _vm.IsGraphMode = false;
        var modeAfterGraphFalse = _vm.ViewMode;

        _vm.IsListMode = true;
        var modeAfterListTrue = _vm.ViewMode;
        var settingAfterList = _ctx.Settings.GetStringValue("wpf.sources.viewMode");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(modeAfterGraphTrue, Is.EqualTo(SourcesRelationsViewMode.Graph));
            Assert.That(settingAfterGraph, Is.EqualTo("Graph"));
            Assert.That(modeAfterGraphFalse, Is.EqualTo(SourcesRelationsViewMode.Graph));
            Assert.That(modeAfterListTrue, Is.EqualTo(SourcesRelationsViewMode.List));
            Assert.That(settingAfterList, Is.EqualTo("List"));
            Assert.That(changed, Does.Contain(nameof(SourcesRelationsViewModel.IsGraphMode)));
            Assert.That(changed, Does.Contain(nameof(SourcesRelationsViewModel.IsListMode)));
        }
    }

    [Test]
    public void SourcesRelationsView_TwoWayRadioButtons_ПереключаютРежимИДержатГрафЛенивым()
    {
        var view = new SourcesRelationsView
        {
            DataContext = _vm,
        };
        view.Measure(new Size(1100, 640));
        view.Arrange(new Rect(0, 0, 1100, 640));
        view.UpdateLayout();

        var radios = FindVisualChildren<RadioButton>(view).ToList();
        var listRadio = radios.First(r => r.IsChecked == true);
        var graphRadio = radios.First(r => r.IsChecked == false);
        var graphPanel = FindVisualChildren<RelationsGraphPanel>(view).Single();
        graphPanel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var graphHost = (DockPanel)graphPanel.FindName("GraphHost");
        var childrenInListMode = graphHost.Children.Count;

        graphRadio.IsChecked = true;
        var modeAfterSelectGraph = _vm.ViewMode;

        listRadio.IsChecked = true;
        var modeAfterSelectList = _vm.ViewMode;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(radios, Has.Count.EqualTo(2));
            Assert.That(childrenInListMode, Is.EqualTo(0));
            Assert.That(modeAfterSelectGraph, Is.EqualTo(SourcesRelationsViewMode.Graph));
            Assert.That(modeAfterSelectList, Is.EqualTo(SourcesRelationsViewMode.List));
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject root)
        where T : System.Windows.DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }
}
