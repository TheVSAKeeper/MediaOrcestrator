using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class SourceSettingsView : UserControl, IView<SourceSettingsViewModel>
{
    public SourceSettingsView()
    {
        InitializeComponent();
    }
}
