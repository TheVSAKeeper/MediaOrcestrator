using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class SourcesRelationsView : UserControl, IView<SourcesRelationsViewModel>
{
    public SourcesRelationsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (DataContext is SourcesRelationsViewModel vm)
            {
                vm.SetViewVisible(IsVisible);
            }
        };
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SourcesRelationsViewModel previous)
        {
            previous.SetViewVisible(false);
        }

        if (e.NewValue is SourcesRelationsViewModel vm)
        {
            vm.SetViewVisible(IsVisible);
            _ = vm.EnsureLoadedAsync();
        }
    }
}
