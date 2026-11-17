using KeepShell.Testing;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.Views.Controls;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class SourceFilterDropDownLeakTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
    }

    [Test]
    public void ItemsSource_ПослеЦиклаLoadedИUnloaded_ПерестаётОбновлятьSummary()
    {
        var items = new ObservableCollection<SourceCheckOptionViewModel>
        {
            new() { SourceId = "s1", Title = "S1", IsChecked = true },
            new() { SourceId = "s2", Title = "S2", IsChecked = true },
        };

        var control = new SourceFilterDropDown
        {
            ItemsSource = items,
        };

        var summaryBeforeLoad = control.Summary;

        items[0].IsChecked = false;
        var summaryChangedWhileDetached = control.Summary;
        items[0].IsChecked = true;

        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        items[0].IsChecked = false;
        var summaryWhileLoaded = control.Summary;

        items.Add(new() { SourceId = "s3", Title = "S3", IsChecked = true });
        var summaryAfterAddWhileLoaded = control.Summary;

        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        items[1].IsChecked = false;
        items.Add(new() { SourceId = "s4", Title = "S4", IsChecked = true });
        var summaryAfterUnload = control.Summary;

        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var summaryAfterReload = control.Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summaryBeforeLoad, Is.EqualTo("2/2"));
            Assert.That(summaryChangedWhileDetached, Is.EqualTo("2/2"));
            Assert.That(summaryWhileLoaded, Is.EqualTo("1/2"));
            Assert.That(summaryAfterAddWhileLoaded, Is.EqualTo("2/3"));
            Assert.That(summaryAfterUnload, Is.EqualTo("2/3"));
            Assert.That(summaryAfterReload, Is.EqualTo("2/4"));
        }
    }

    [Test]
    public void Unloaded_ПослеСборкиМусора_НеУдерживаетКонтролВЖивых()
    {
        var items = new ObservableCollection<SourceCheckOptionViewModel>
        {
            new() { SourceId = "s1", Title = "S1", IsChecked = true },
            new() { SourceId = "s2", Title = "S2", IsChecked = true },
        };

        var weakRef = CreateRunAndUnload(items);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        GC.KeepAlive(items);

        Assert.That(weakRef.IsAlive, Is.False);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateRunAndUnload(ObservableCollection<SourceCheckOptionViewModel> items)
    {
        var control = new SourceFilterDropDown
        {
            ItemsSource = items,
        };

        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        items[0].IsChecked = false;
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

        return new(control);
    }
}
