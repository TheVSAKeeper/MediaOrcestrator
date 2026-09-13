using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class SourcesRelationsView : UserControl, IView<SourcesRelationsViewModel>
{
    public SourcesRelationsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is SourcesRelationsViewModel vm)
        {
            _ = vm.EnsureLoadedAsync();
        }
    }
}
