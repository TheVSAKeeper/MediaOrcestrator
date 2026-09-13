using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Dialogs;

public partial class AddSourceView : UserControl, IView<AddSourceViewModel>
{
    public AddSourceView()
    {
        InitializeComponent();
    }
}
