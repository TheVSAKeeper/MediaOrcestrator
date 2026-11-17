using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Dialogs;

public partial class WinFormsImportDialogView : UserControl, IView<WinFormsImportDialogViewModel>
{
    public WinFormsImportDialogView()
    {
        InitializeComponent();
    }
}
