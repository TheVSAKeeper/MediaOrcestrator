using MahApps.Metro.IconPacks;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views.Controls;

public partial class HintRow : UserControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(PackIconLucideKind),
        typeof(HintRow),
        new PropertyMetadata(PackIconLucideKind.Info));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(HintRow),
        new PropertyMetadata(string.Empty));

    public HintRow()
    {
        InitializeComponent();
    }

    public PackIconLucideKind Icon
    {
        get => (PackIconLucideKind)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
