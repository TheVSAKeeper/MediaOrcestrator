using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Controls;

public partial class FilterChecklist : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(FilterChecklist),
        new PropertyMetadata(null));

    public static readonly DependencyProperty MaxListHeightProperty = DependencyProperty.Register(
        nameof(MaxListHeight),
        typeof(double),
        typeof(FilterChecklist),
        new PropertyMetadata(320d));

    public static readonly DependencyProperty EmptyHintProperty = DependencyProperty.Register(
        nameof(EmptyHint),
        typeof(string),
        typeof(FilterChecklist),
        new PropertyMetadata(null));

    public static readonly DependencyProperty EmptyHintVisibilityProperty = DependencyProperty.Register(
        nameof(EmptyHintVisibility),
        typeof(Visibility),
        typeof(FilterChecklist),
        new PropertyMetadata(Visibility.Collapsed));

    public FilterChecklist()
    {
        InitializeComponent();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public double MaxListHeight
    {
        get => (double)GetValue(MaxListHeightProperty);
        set => SetValue(MaxListHeightProperty, value);
    }

    public string? EmptyHint
    {
        get => (string?)GetValue(EmptyHintProperty);
        set => SetValue(EmptyHintProperty, value);
    }

    public Visibility EmptyHintVisibility
    {
        get => (Visibility)GetValue(EmptyHintVisibilityProperty);
        set => SetValue(EmptyHintVisibilityProperty, value);
    }
}
