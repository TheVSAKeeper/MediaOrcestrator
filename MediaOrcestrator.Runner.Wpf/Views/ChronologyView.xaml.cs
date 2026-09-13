using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class ChronologyView : UserControl, IView<ChronologyViewModel>
{
    public ChronologyView()
    {
        InitializeComponent();
    }

    private void OnListRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox list
            && e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(list, source) is ListBoxItem item)
        {
            item.IsSelected = true;
        }
    }
}
