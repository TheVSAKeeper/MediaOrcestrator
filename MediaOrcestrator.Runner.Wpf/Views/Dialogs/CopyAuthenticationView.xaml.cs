using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Dialogs;

public partial class CopyAuthenticationView : UserControl, IView<CopyAuthenticationViewModel>
{
    public CopyAuthenticationView()
    {
        InitializeComponent();
    }
}
