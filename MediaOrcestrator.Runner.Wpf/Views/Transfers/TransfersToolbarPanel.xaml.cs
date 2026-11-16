using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Transfers;

public partial class TransfersToolbarPanel : UserControl
{
    public TransfersToolbarPanel()
    {
        InitializeComponent();
    }

    internal void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
