using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Dialogs;

public partial class ColorPickerView : UserControl, IView<ColorPickerViewModel>
{
    public ColorPickerView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HexBox.Focus();
        HexBox.SelectAll();
    }
}
