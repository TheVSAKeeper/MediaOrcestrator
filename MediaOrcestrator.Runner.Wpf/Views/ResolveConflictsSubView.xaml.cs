using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class ResolveConflictsSubView : UserControl, IView<ResolveConflictsSubViewModel>
{
    public ResolveConflictsSubView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ResolveConflictsSubViewModel vm)
        {
            vm.EnsureScopeLoaded();
        }
    }
}
