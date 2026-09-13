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

    public MediaView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyInspectorLayout();

        if (DataContext is MediaViewModel vm)
        {
            await vm.EnsureLoadedAsync();
        }
    }

    private async void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = e.NewValue as MediaViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyInspectorLayout();
            await _vm.EnsureLoadedAsync();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaViewModel.IsInspectorCollapsed))
        {
            ApplyInspectorLayout();
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
            InspectorColumn.SetCurrentValue(ColumnDefinition.MinWidthProperty, RailWidth);
            InspectorColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(RailWidth));
            SplitterColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(0));
        }
        else
        {
            InspectorColumn.SetCurrentValue(ColumnDefinition.MinWidthProperty, MinPanelWidth);
            InspectorColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(_vm.InspectorWidth));
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
