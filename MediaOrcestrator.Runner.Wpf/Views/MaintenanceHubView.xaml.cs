using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class MaintenanceHubView : UserControl, IView<MaintenanceHubViewModel>
{
    public MaintenanceHubView()
    {
        InitializeComponent();
    }
}
