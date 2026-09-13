using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class ToolsView : UserControl, IView<ToolsViewModel>
{
    public ToolsView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ToolsViewModel vm)
        {
            await vm.EnsureCheckedAsync();
        }
    }
}
