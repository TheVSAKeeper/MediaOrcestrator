using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Dialogs;

public partial class CoverTemplateInspectorPanel : UserControl
{
    public CoverTemplateInspectorPanel()
    {
        InitializeComponent();
    }

    internal void FocusProfileName() => ProfileNameBox.Focus();
}
