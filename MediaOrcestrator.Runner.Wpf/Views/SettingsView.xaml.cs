using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class SettingsView : UserControl, IView<SettingsViewModel>
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void SuppressAutoScroll(object sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }
}
