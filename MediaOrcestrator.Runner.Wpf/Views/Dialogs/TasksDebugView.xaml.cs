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

    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);

        return new(
            double.IsPositiveInfinity(availableSize.Width) ? 800 : availableSize.Width,
            double.IsPositiveInfinity(availableSize.Height) ? 520 : availableSize.Height);
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
