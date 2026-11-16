using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.Views.Media;

public partial class MediaInspectorPanel : UserControl
{
    private MediaViewModel? _vm;

    public MediaInspectorPanel()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = e.NewValue as MediaViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaViewModel.IsInspectorCollapsed))
        {
            MoveInspectorFocusOnToggle();
        }
    }

    private void MoveInspectorFocusOnToggle()
    {
        if (_vm is null || Keyboard.FocusedElement is not DependencyObject focused || !InspectorBorder.IsAncestorOf(focused))
        {
            return;
        }

        var target = _vm.IsInspectorCollapsed ? (Control)RailExpandButton : HeaderCollapseButton;

        target.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => target.Focus()));
    }

    private void OnPreviewImageTargetUpdated(object? sender, DataTransferEventArgs e)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        if (!SystemParameters.ClientAreaAnimation)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.SetCurrentValue(OpacityProperty, 1d);
            return;
        }

        var animation = new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(250));
        element.BeginAnimation(OpacityProperty, animation);
    }
}
