using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class DatabaseCleanupView : UserControl, IView<DatabaseCleanupViewModel>
{
    public DatabaseCleanupView()
    {
        InitializeComponent();
    }
}
