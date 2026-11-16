using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class TransfersView : UserControl, IView<TransfersViewModel>
{
    private Point _dragStartPoint;
    private TransferMediaGroupViewModel? _dragCandidate;
    private bool _isDragging;
    private TransfersViewModel? _vm;

    public TransfersView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.SearchFocusRequested -= OnSearchFocusRequested;
        }

        _vm = e.NewValue as TransfersViewModel;

        if (_vm is not null)
        {
            _vm.SearchFocusRequested += OnSearchFocusRequested;
        }
    }

    private void OnSearchFocusRequested(object? sender, EventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ActualWidth <= 0)
        {
            return;
        }

        JournalBorder.SetCurrentValue(WidthProperty, Math.Min(340d, Math.Max(220d, ActualWidth * 0.40)));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is TransfersViewModel vm)
        {
            await vm.EnsureLoadedAsync();
        }
    }

    private void OnTreePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
        _isDragging = false;

        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindAncestor<CheckBox>(source) is not null || FindAncestor<ToggleButton>(source) is not null)
        {
            return;
        }

        var item = FindAncestor<TreeViewItem>(source);

        if (item?.DataContext is TransferMediaGroupViewModel group)
        {
            _dragCandidate = group;
            _dragStartPoint = e.GetPosition(TransfersTreeView);
        }
    }

    private void OnTreePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging || _dragCandidate is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(TransfersTreeView);
        var dx = Math.Abs(pos.X - _dragStartPoint.X);
        var dy = Math.Abs(pos.Y - _dragStartPoint.Y);

        if (dx < SystemParameters.MinimumHorizontalDragDistance && dy < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDragging = true;
        var data = new DataObject(typeof(TransferMediaGroupViewModel), _dragCandidate);

        try
        {
            DragDrop.DoDragDrop(TransfersTreeView, data, DragDropEffects.Move);
        }
        finally
        {
            _isDragging = false;
            _dragCandidate = null;
        }
    }

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(TransferMediaGroupViewModel)) || ResolveTargetGroup(e) is null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not TransfersViewModel vm)
        {
            return;
        }

        if (e.Data.GetData(typeof(TransferMediaGroupViewModel)) is not TransferMediaGroupViewModel source)
        {
            return;
        }

        var resolved = ResolveTargetGroup(e);

        if (resolved is null)
        {
            return;
        }

        var (target, insertBelow) = resolved.Value;
        vm.MoveGroup(source, target, insertBelow);
        e.Handled = true;
    }

    private (TransferMediaGroupViewModel Group, bool InsertBelow)? ResolveTargetGroup(DragEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return null;
        }

        var item = FindGroupContainer(source);

        if (item?.DataContext is not TransferMediaGroupViewModel group)
        {
            return null;
        }

        var pos = e.GetPosition(item);
        var insertBelow = pos.Y > item.ActualHeight / 2;
        return (group, insertBelow);
    }

    private static TreeViewItem? FindGroupContainer(DependencyObject source)
    {
        var current = FindAncestor<TreeViewItem>(source);

        while (current is not null)
        {
            if (current.DataContext is TransferMediaGroupViewModel)
            {
                return current;
            }

            var parent = VisualTreeHelper.GetParent(current);
            current = parent is null ? null : FindAncestor<TreeViewItem>(parent);
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null and not T)
        {
            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return source as T;
    }
}
