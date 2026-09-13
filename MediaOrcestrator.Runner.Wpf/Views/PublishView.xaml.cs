using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class PublishView : UserControl, IView<PublishViewModel>
{
    public PublishView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PublishViewModel vm)
        {
            vm.Reload();
        }
    }

    private void OnDescriptionTemplateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo)
        {
            return;
        }

        if (combo.SelectedItem is not DescriptionTemplateViewModel template
            || DataContext is not PublishViewModel vm)
        {
            return;
        }

        vm.ApplyDescriptionTemplateCommand.Execute(template);
        combo.SelectedIndex = -1;
    }
}
