using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class OperationsView : UserControl, IView<OperationsViewModel>
{
    public OperationsView()
    {
        InitializeComponent();
    }
}
