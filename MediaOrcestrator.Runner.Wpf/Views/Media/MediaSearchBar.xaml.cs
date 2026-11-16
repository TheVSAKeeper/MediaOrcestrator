using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Media;

public partial class MediaSearchBar : UserControl
{
    public MediaSearchBar()
    {
        InitializeComponent();
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
