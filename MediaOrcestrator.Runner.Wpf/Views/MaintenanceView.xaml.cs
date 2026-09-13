using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class MaintenanceView : UserControl, IView<MaintenanceViewModel>
{
    public MaintenanceView()
    {
        InitializeComponent();
    }
}
