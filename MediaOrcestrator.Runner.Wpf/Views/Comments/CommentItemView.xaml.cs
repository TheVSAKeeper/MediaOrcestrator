using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Comments;

public partial class CommentItemView : UserControl, IView<CommentItemViewModel>
{
    public CommentItemView()
    {
        InitializeComponent();
    }
}
