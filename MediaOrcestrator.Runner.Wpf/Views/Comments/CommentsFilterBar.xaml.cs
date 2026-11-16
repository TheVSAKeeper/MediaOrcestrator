using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Comments;

public partial class CommentsFilterBar : UserControl
{
    public CommentsFilterBar()
    {
        InitializeComponent();
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
