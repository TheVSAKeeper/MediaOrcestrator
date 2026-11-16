using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class CommentsView : UserControl, IView<CommentsViewModel>
{
    private CommentsViewModel? _vm;

    public CommentsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CommentsViewModel vm)
        {
            vm.SetViewVisible(IsVisible);
            await vm.EnsureLoadedAsync();
        }

        ReplyEditorTemplateWarmer.Prewarm();
    }

    private async void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.SearchFocusRequested -= OnSearchFocusRequested;
        }

        _vm = e.NewValue as CommentsViewModel;

        if (_vm is not null)
        {
            _vm.SearchFocusRequested += OnSearchFocusRequested;

            if (IsLoaded)
            {
                _vm.SetViewVisible(IsVisible);
                await _vm.EnsureLoadedAsync();
            }
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is CommentsViewModel vm)
        {
            vm.SetViewVisible(IsVisible);
        }
    }

    private void OnSearchFocusRequested(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FilterBar.FocusSearch));
    }
}
