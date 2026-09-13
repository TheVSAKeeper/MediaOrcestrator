using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Comments;

public partial class ReplyEditorView : UserControl, IView<ReplyEditorViewModel>
{
    public ReplyEditorView()
    {
        InitializeComponent();
    }
}
