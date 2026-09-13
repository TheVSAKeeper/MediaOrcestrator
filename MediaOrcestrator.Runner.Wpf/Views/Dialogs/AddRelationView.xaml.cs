using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Dialogs;

public partial class AddRelationView : UserControl, IView<AddRelationViewModel>
{
    public AddRelationView()
    {
        InitializeComponent();
    }
}
