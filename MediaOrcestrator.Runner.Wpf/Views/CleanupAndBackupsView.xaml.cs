using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class CleanupAndBackupsView : UserControl, IView<CleanupAndBackupsViewModel>
{
    public CleanupAndBackupsView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CleanupAndBackupsViewModel vm)
        {
            await vm.RefreshAsync();
        }
    }
}
