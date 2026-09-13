using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class TasksView : UserControl, IView<TasksViewModel>
{
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact),
        typeof(bool),
        typeof(TasksView),
        new PropertyMetadata(false, OnCompactChanged));

    private static readonly DependencyPropertyKey CompactVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CompactVisibility),
        typeof(Visibility),
        typeof(TasksView),
        new PropertyMetadata(Visibility.Collapsed));

    public static readonly DependencyProperty CompactVisibilityProperty = CompactVisibilityPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey FullVisibilityPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(FullVisibility),
        typeof(Visibility),
        typeof(TasksView),
        new PropertyMetadata(Visibility.Visible));

    public static readonly DependencyProperty FullVisibilityProperty = FullVisibilityPropertyKey.DependencyProperty;

    public TasksView()
    {
        InitializeComponent();
    }

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public Visibility CompactVisibility => (Visibility)GetValue(CompactVisibilityProperty);

    public Visibility FullVisibility => (Visibility)GetValue(FullVisibilityProperty);

    private static void OnCompactChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (TasksView)d;
        var compact = (bool)e.NewValue;
        view.SetValue(CompactVisibilityPropertyKey, compact ? Visibility.Visible : Visibility.Collapsed);
        view.SetValue(FullVisibilityPropertyKey, compact ? Visibility.Collapsed : Visibility.Visible);
    }
}
