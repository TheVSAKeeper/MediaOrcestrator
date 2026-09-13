using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.Views.Controls;

public partial class Avatar : UserControl
{
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(Avatar),
        new PropertyMetadata(28d));

    public static readonly DependencyProperty InitialProperty = DependencyProperty.Register(
        nameof(Initial),
        typeof(string),
        typeof(Avatar),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource),
        typeof(ImageSource),
        typeof(Avatar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty InitialFontSizeProperty = DependencyProperty.Register(
        nameof(InitialFontSize),
        typeof(double),
        typeof(Avatar),
        new PropertyMetadata(13d));

    public Avatar()
    {
        InitializeComponent();
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public string? Initial
    {
        get => (string?)GetValue(InitialProperty);
        set => SetValue(InitialProperty, value);
    }

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public double InitialFontSize
    {
        get => (double)GetValue(InitialFontSizeProperty);
        set => SetValue(InitialFontSizeProperty, value);
    }
}
