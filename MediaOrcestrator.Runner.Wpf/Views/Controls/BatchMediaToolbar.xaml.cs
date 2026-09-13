using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.Views.Controls;

public partial class BatchMediaToolbar : UserControl
{
    public static readonly DependencyProperty MediasSummaryProperty = DependencyProperty.Register(
        nameof(MediasSummary),
        typeof(string),
        typeof(BatchMediaToolbar),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LoadAllMediaCommandProperty = DependencyProperty.Register(
        nameof(LoadAllMediaCommand),
        typeof(ICommand),
        typeof(BatchMediaToolbar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ClearMediaCommandProperty = DependencyProperty.Register(
        nameof(ClearMediaCommand),
        typeof(ICommand),
        typeof(BatchMediaToolbar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty HasMediasProperty = DependencyProperty.Register(
        nameof(HasMedias),
        typeof(bool),
        typeof(BatchMediaToolbar),
        new PropertyMetadata(false));

    public static readonly DependencyProperty CanEditParametersProperty = DependencyProperty.Register(
        nameof(CanEditParameters),
        typeof(bool),
        typeof(BatchMediaToolbar),
        new PropertyMetadata(false));

    public static readonly DependencyProperty StopOnErrorProperty = DependencyProperty.Register(
        nameof(StopOnError),
        typeof(bool),
        typeof(BatchMediaToolbar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public BatchMediaToolbar()
    {
        InitializeComponent();
    }

    public string MediasSummary
    {
        get => (string)GetValue(MediasSummaryProperty);
        set => SetValue(MediasSummaryProperty, value);
    }

    public ICommand? LoadAllMediaCommand
    {
        get => (ICommand?)GetValue(LoadAllMediaCommandProperty);
        set => SetValue(LoadAllMediaCommandProperty, value);
    }

    public ICommand? ClearMediaCommand
    {
        get => (ICommand?)GetValue(ClearMediaCommandProperty);
        set => SetValue(ClearMediaCommandProperty, value);
    }

    public bool HasMedias
    {
        get => (bool)GetValue(HasMediasProperty);
        set => SetValue(HasMediasProperty, value);
    }

    public bool CanEditParameters
    {
        get => (bool)GetValue(CanEditParametersProperty);
        set => SetValue(CanEditParametersProperty, value);
    }

    public bool StopOnError
    {
        get => (bool)GetValue(StopOnErrorProperty);
        set => SetValue(StopOnErrorProperty, value);
    }
}
