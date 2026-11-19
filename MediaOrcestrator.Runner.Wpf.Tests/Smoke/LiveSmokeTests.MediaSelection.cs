using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.Tests.Smoke;

public sealed partial class LiveSmokeTests
{
    [Test]
    public void Передачи_ОткрытьВМедиаПриАктивномПоиске_ПоказываетИВыделяетЦелевуюСтроку()
    {
        NavigateToPage("transfers");
        Settle();
        var media = _services.GetRequiredService<MediaViewModel>();
        var vm = media.Grid;
        var originalMedias = vm.Rows.Select(row => row.Media).ToList();
        var originalSearch = media.Filter.SearchText;
        var originalCollapsed = media.IsInspectorCollapsed;
        var visible = new Media { Id = "transfer-visible", Title = "Архив" };
        var target = new Media { Id = "transfer-target", Title = "Премьера" };

        try
        {
            vm.ApplyDomainData([visible, target]);
            media.Filter.SetSearchImmediateForAutomation(visible.Title);
            Assert.That(vm.RowsView.Cast<MediaRowViewModel>().Select(row => row.Media), Is.EqualTo(new[] { visible }));

            _services.GetRequiredService<TransfersViewModel>().OpenInMedia(target);
            Settle();

            var grid = Descendants<DataGrid>(_window).SingleOrDefault(item => item.IsVisible && item.Name == "MediaGrid");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(grid?.SelectedItems.Cast<MediaRowViewModel>().Select(row => row.Media), Is.EqualTo(new[] { target }));
                Assert.That(vm.Focused?.Media, Is.SameAs(target));
                Assert.That(media.IsInspectorCollapsed, Is.False);
                Assert.That(media.Filter.SearchText, Is.EqualTo(visible.Title));
            }

            media.Filter.SetSearchImmediateForAutomation("Нет совпадений");
            Assert.That(vm.RowsView.IsEmpty, Is.True);
            Assert.That(vm.SelectedCount, Is.Zero);
        }
        finally
        {
            vm.ClearSelectionCommand.Execute(null);
            media.IsInspectorCollapsed = originalCollapsed;
            media.Filter.SetSearchImmediateForAutomation(originalSearch);
            vm.ApplyDomainData(originalMedias);
            Settle();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Медиа_ВыборВсехСтрок_УчитываетФильтрИВиртуализацию(bool grouped)
    {
        NavigateToPage("media");
        Settle();
        var media = _services.GetRequiredService<MediaViewModel>();
        var vm = media.Grid;
        var grid = Descendants<DataGrid>(_window).First(item => item.IsVisible && item.Name == "MediaGrid");
        var originalMedias = vm.Rows.Select(row => row.Media).ToList();
        var originalSearch = media.Filter.SearchText;
        var originalGrouping = vm.IsGrouped;
        var originalCollapsed = media.IsInspectorCollapsed;
        var medias = Enumerable.Range(1, 100)
            .Select(i => new Media
            {
                Id = $"selection-{i}",
                Title = $"Хроники / #{i} {(i % 2 == 0 ? "Архив" : "Премьера")}",
            })
            .ToList();

        try
        {
            vm.IsGrouped = grouped;
            vm.ApplyDomainData(medias);
            media.Filter.SetSearchImmediateForAutomation("Премьера");
            Settle();

            Assert.That(grid.Items, Has.Count.EqualTo(50));
            Assert.That(grid.ItemContainerGenerator.ContainerFromIndex(49), Is.Null);
            ApplicationCommands.SelectAll.Execute(null, grid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(grid.SelectedItems, Has.Count.EqualTo(50));
                Assert.That(vm.SelectedCount, Is.EqualTo(50));
            }

            var counter = Descendants<StackPanel>(_window).Single(element =>
                element.IsVisible && Equals(element.ToolTip, "Выделено строк в матрице"));
            Assert.That(AutomationProperties.GetName(counter), Is.EqualTo("Выделено: 50"));

            var keyboardOpening = (ContextMenuEventArgs)Activator.CreateInstance(
                typeof(ContextMenuEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, args: [grid, true, -1d, -1d], culture: null)!;
            grid.RaiseEvent(keyboardOpening);
            Assert.That(keyboardOpening.Handled, Is.False);
            Assert.That(vm.ContextMenuItems, Is.Not.Empty);
            Assert.That(grid.SelectedItems, Has.Count.EqualTo(50));

            var firstSelected = (MediaRowViewModel)grid.Items[0];
            var lastSelected = (MediaRowViewModel)grid.Items[49];
            grid.ScrollIntoView(lastSelected);
            Settle();
            var container = grid.ItemContainerGenerator.ContainerFromItem(lastSelected) as DataGridRow;
            Assert.That(container?.IsSelected, Is.True);
            grid.ScrollIntoView(firstSelected);
            Settle();
            Assert.That((grid.ItemContainerGenerator.ContainerFromItem(firstSelected) as DataGridRow)?.IsSelected, Is.True);
            Assert.That(grid.ItemContainerGenerator.ContainerFromItem(lastSelected), Is.Null);

            vm.IsGrouped = !grouped;
            Settle();
            Assert.That(grid.SelectedItems, Has.Count.EqualTo(50));
            Assert.That(vm.SelectedCount, Is.EqualTo(50));

            vm.BuildContextMenu(lastSelected, null);
            Assert.That(vm.SelectedCount, Is.EqualTo(50));

            var rowsView = (ListCollectionView)vm.RowsView;
            var originalSort = rowsView.CustomSort;
            rowsView.CustomSort = Comparer<MediaRowViewModel>.Create((left, right) =>
                string.CompareOrdinal(right.Title, left.Title));
            Assert.That(grid.SelectedItems, Has.Count.EqualTo(50));
            Assert.That(vm.SelectedCount, Is.EqualTo(50));
            rowsView.CustomSort = originalSort;

            media.Filter.SetSearchImmediateForAutomation(lastSelected.Title);
            Assert.That(grid.SelectedItems.Cast<MediaRowViewModel>(), Is.EqualTo(new[] { lastSelected }));
            Assert.That(vm.SelectedCount, Is.EqualTo(1));

            media.Filter.SetSearchImmediateForAutomation("Архив");
            Settle();
            Assert.That(grid.SelectedItems, Is.Empty);
            Assert.That(vm.SelectedCount, Is.Zero);

            var first = (MediaRowViewModel)grid.Items[0];
            var last = (MediaRowViewModel)grid.Items[49];
            grid.ScrollIntoView(first);
            Settle();
            Assert.That(grid.ItemContainerGenerator.ContainerFromItem(last), Is.Null);
            ApplicationCommands.SelectAll.Execute(null, grid);
            Assert.That(grid.SelectedItems.Contains(last), Is.True);
            vm.ClearSelectionCommand.Execute(null);
            Assert.That(grid.SelectedItems, Is.Empty);
            Assert.That(vm.SelectedCount, Is.Zero);
            grid.ScrollIntoView(last);
            Settle();
            Assert.That((grid.ItemContainerGenerator.ContainerFromItem(last) as DataGridRow)?.IsSelected, Is.False);

            grid.SelectedItems.Add(first);
            vm.BuildContextMenu(last, null);
            Assert.That(grid.SelectedItems.Cast<MediaRowViewModel>(), Is.EqualTo(new[] { last }));
            Assert.That(vm.SelectedCount, Is.EqualTo(1));

            grid.SelectedItems.Add(first);
            vm.OpenInspectorCommand.Execute(last);
            Assert.That(grid.SelectedItems.Cast<MediaRowViewModel>(), Is.EqualTo(new[] { last }));
            Assert.That(vm.SelectedCount, Is.EqualTo(1));
        }
        finally
        {
            vm.ClearSelectionCommand.Execute(null);
            media.IsInspectorCollapsed = originalCollapsed;
            media.Filter.SetSearchImmediateForAutomation(originalSearch);
            vm.IsGrouped = originalGrouping;
            vm.ApplyDomainData(originalMedias);
            Settle();
        }
    }
}
