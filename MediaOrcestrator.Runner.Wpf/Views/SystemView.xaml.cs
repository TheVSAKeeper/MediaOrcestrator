using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class SystemView : UserControl, IView<SystemViewModel>
{
    public SystemView()
    {
        InitializeComponent();
    }
}
