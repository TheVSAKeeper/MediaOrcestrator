using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class AboutView : UserControl, IView<AboutViewModel>
{
    public AboutView()
    {
        InitializeComponent();
    }
}
