using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class MergeDuplicatesSubView : UserControl, IView<MergeDuplicatesSubViewModel>
{
    public MergeDuplicatesSubView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MergeDuplicatesSubViewModel vm)
        {
            vm.EnsureScopeLoaded();
        }
    }
}
