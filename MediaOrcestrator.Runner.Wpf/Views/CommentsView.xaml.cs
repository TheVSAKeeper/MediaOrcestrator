using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class CommentsView : UserControl, IView<CommentsViewModel>
{
    public CommentsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CommentsViewModel vm)
        {
            await vm.EnsureLoadedAsync();
        }

        ReplyEditorTemplateWarmer.Prewarm();
    }

    private async void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is CommentsViewModel vm && IsLoaded)
        {
            await vm.EnsureLoadedAsync();
        }
    }
}
