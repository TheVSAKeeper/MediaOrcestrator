using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class MediaView : UserControl, IView<MediaViewModel>
{
    private const double RailWidth = 54;

    private const double MinPanelWidth = MediaViewModel.MinInspectorWidth;

    private MediaViewModel? _vm;
    private Window? _hostWindow;

    public MediaView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachHostWindow();
        UpdateViewportWidth();
        ApplyInspectorLayout();

        if (DataContext is MediaViewModel vm)
        {
            await vm.EnsureLoadedAsync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachHostWindow();
    }

    private async void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.SearchFocusRequested -= OnSearchFocusRequested;
        }

        _vm = e.NewValue as MediaViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            _vm.SearchFocusRequested += OnSearchFocusRequested;

            if (IsLoaded)
            {
                UpdateViewportWidth();
            }

            ApplyInspectorLayout();
            await _vm.EnsureLoadedAsync();
        }
    }

    private void OnSearchFocusRequested(object? sender, EventArgs e)
    {
        SearchBar.FocusSearch();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaViewModel.IsInspectorCollapsed))
        {
            ApplyInspectorLayout();
        }
    }

    private void OnHostWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewportWidth();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewportWidth();
        ApplyInspectorLayout();
    }

    private void AttachHostWindow()
    {
        var window = Window.GetWindow(this);

        if (ReferenceEquals(_hostWindow, window))
        {
            return;
        }

        DetachHostWindow();

        if (window is not null)
        {
            window.SizeChanged += OnHostWindowSizeChanged;
            _hostWindow = window;
        }
    }

    private void DetachHostWindow()
    {
        if (_hostWindow is not null)
        {
            _hostWindow.SizeChanged -= OnHostWindowSizeChanged;
            _hostWindow = null;
        }
    }

    private void UpdateViewportWidth()
    {
        if (_vm is null)
        {
            return;
        }

        var hostWidth = _hostWindow?.ActualWidth ?? 0;

        if (hostWidth <= 0 && ActualWidth > 0)
        {
            hostWidth = ActualWidth + 240;
        }

        if (hostWidth > 0)
        {
            _vm.UpdateViewportWidth(hostWidth);
        }
    }

    private void ApplyInspectorLayout()
    {
        if (_vm is null)
        {
            return;
        }

        if (_vm.IsInspectorCollapsed)
        {
            InspectorColumn.SetCurrentValue(ColumnDefinition.MinWidthProperty, (double)RailWidth);
            InspectorColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(RailWidth));
            SplitterColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(0));
        }
        else
        {
            var width = _vm.InspectorWidth;

            if (ActualWidth > 0)
            {
                var maxForInspector = Math.Max(MinPanelWidth, ActualWidth - 266);
                width = Math.Min(width, maxForInspector);
            }

            InspectorColumn.SetCurrentValue(ColumnDefinition.MinWidthProperty, (double)MinPanelWidth);
            InspectorColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(width));
            SplitterColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(6));
        }
    }

    private void OnActionMenuItemClick(object sender, RoutedEventArgs e)
    {
        TransferButton.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        MoreActionsButton.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
    }

    private void OnInspectorSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_vm is null || _vm.IsInspectorCollapsed)
        {
            return;
        }

        _vm.SetInspectorWidth(InspectorColumn.ActualWidth);

        ApplyInspectorLayout();
    }
}
