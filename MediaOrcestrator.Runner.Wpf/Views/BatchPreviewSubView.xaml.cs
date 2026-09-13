using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class BatchPreviewSubView : UserControl, IView<BatchPreviewSubViewModel>
{
    public BatchPreviewSubView()
    {
        InitializeComponent();
    }
}
