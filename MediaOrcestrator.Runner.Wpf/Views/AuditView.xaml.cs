using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class AuditView : UserControl, IView<AuditViewModel>
{
    public AuditView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AuditViewModel vm)
        {
            await vm.EnsureLoadedAsync();
        }
    }
}
