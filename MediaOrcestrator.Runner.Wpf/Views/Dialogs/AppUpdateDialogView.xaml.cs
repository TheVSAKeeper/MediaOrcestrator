using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Dialogs;

public partial class AppUpdateDialogView : UserControl, IView<AppUpdateDialogViewModel>
{
    public AppUpdateDialogView()
    {
        InitializeComponent();
    }
}
