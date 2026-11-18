using System.Windows.Controls;
using MediaOrcestrator.Runner.Wpf.ViewModels;

namespace MediaOrcestrator.Runner.Wpf.Views.Dialogs;

public partial class AuthenticationPromptView : UserControl, IView<AuthenticationPromptViewModel>
{
    public AuthenticationPromptView()
    {
        InitializeComponent();
    }
}
