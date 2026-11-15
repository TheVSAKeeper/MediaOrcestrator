using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class CleanupAndBackupsView : UserControl, IView<CleanupAndBackupsViewModel>
{
    public CleanupAndBackupsView()
    {
        InitializeComponent();
    }
}
