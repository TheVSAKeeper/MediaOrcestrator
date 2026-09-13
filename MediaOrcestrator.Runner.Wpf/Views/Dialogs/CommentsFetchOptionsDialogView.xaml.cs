using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Dialogs;

public partial class CommentsFetchOptionsDialogView : UserControl, IView<CommentsFetchOptionsDialogViewModel>
{
    public CommentsFetchOptionsDialogView()
    {
        InitializeComponent();
    }
}
