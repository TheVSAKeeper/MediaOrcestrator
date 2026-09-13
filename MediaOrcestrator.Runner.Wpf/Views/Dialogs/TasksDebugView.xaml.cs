using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Dialogs;

public partial class TasksDebugView : UserControl, IView<TasksDebugViewModel>
{
    public TasksDebugView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DumpBox.Focus();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            DumpBox.SelectAll();
            DumpBox.Copy();
        }
        catch
        {
        }
    }
}
