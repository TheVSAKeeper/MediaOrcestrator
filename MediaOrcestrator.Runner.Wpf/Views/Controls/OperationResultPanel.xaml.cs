using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace MediaOrcestrator.Runner.Wpf.Views.Controls;

[ContentProperty(nameof(GridContent))]
public partial class OperationResultPanel : UserControl
{
    public static readonly DependencyProperty HeaderLeftProperty = DependencyProperty.Register(
        nameof(HeaderLeft),
        typeof(object),
        typeof(OperationResultPanel),
        new PropertyMetadata(null));

    public static readonly DependencyProperty GridContentProperty = DependencyProperty.Register(
        nameof(GridContent),
        typeof(object),
        typeof(OperationResultPanel),
        new PropertyMetadata(null));

    public static readonly DependencyProperty SelectAllCommandProperty = DependencyProperty.Register(
        nameof(SelectAllCommand),
        typeof(ICommand),
        typeof(OperationResultPanel),
        new PropertyMetadata(null));

    public static readonly DependencyProperty SelectNoneCommandProperty = DependencyProperty.Register(
        nameof(SelectNoneCommand),
        typeof(ICommand),
        typeof(OperationResultPanel),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CanSelectProperty = DependencyProperty.Register(
        nameof(CanSelect),
        typeof(bool),
        typeof(OperationResultPanel),
        new PropertyMetadata(false));

    public static readonly DependencyProperty JournalSourceProperty = DependencyProperty.Register(
        nameof(JournalSource),
        typeof(IEnumerable),
        typeof(OperationResultPanel),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ClearJournalCommandProperty = DependencyProperty.Register(
        nameof(ClearJournalCommand),
        typeof(ICommand),
        typeof(OperationResultPanel),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IsJournalOpenProperty = DependencyProperty.Register(
        nameof(IsJournalOpen),
        typeof(bool),
        typeof(OperationResultPanel),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact),
        typeof(bool),
        typeof(OperationResultPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.Register(
        nameof(IsEmpty),
        typeof(bool),
        typeof(OperationResultPanel),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        nameof(IsLoading),
        typeof(bool),
        typeof(OperationResultPanel),
        new PropertyMetadata(false));

    public static readonly DependencyProperty EmptyContentProperty = DependencyProperty.Register(
        nameof(EmptyContent),
        typeof(object),
        typeof(OperationResultPanel),
        new PropertyMetadata(null));

    public static readonly DependencyProperty LoadingTextProperty = DependencyProperty.Register(
        nameof(LoadingText),
        typeof(string),
        typeof(OperationResultPanel),
        new PropertyMetadata("Загрузка…"));

    public static readonly DependencyProperty ShowCompactToggleProperty = DependencyProperty.Register(
        nameof(ShowCompactToggle),
        typeof(bool),
        typeof(OperationResultPanel),
        new PropertyMetadata(true));

    public OperationResultPanel()
    {
        InitializeComponent();
    }

    public object? HeaderLeft
    {
        get => GetValue(HeaderLeftProperty);
        set => SetValue(HeaderLeftProperty, value);
    }

    public object? GridContent
    {
        get => GetValue(GridContentProperty);
        set => SetValue(GridContentProperty, value);
    }

    public ICommand? SelectAllCommand
    {
        get => (ICommand?)GetValue(SelectAllCommandProperty);
        set => SetValue(SelectAllCommandProperty, value);
    }

    public ICommand? SelectNoneCommand
    {
        get => (ICommand?)GetValue(SelectNoneCommandProperty);
        set => SetValue(SelectNoneCommandProperty, value);
    }

    public bool CanSelect
    {
        get => (bool)GetValue(CanSelectProperty);
        set => SetValue(CanSelectProperty, value);
    }

    public IEnumerable? JournalSource
    {
        get => (IEnumerable?)GetValue(JournalSourceProperty);
        set => SetValue(JournalSourceProperty, value);
    }

    public ICommand? ClearJournalCommand
    {
        get => (ICommand?)GetValue(ClearJournalCommandProperty);
        set => SetValue(ClearJournalCommandProperty, value);
    }

    public bool IsJournalOpen
    {
        get => (bool)GetValue(IsJournalOpenProperty);
        set => SetValue(IsJournalOpenProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public bool IsEmpty
    {
        get => (bool)GetValue(IsEmptyProperty);
        set => SetValue(IsEmptyProperty, value);
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public object? EmptyContent
    {
        get => GetValue(EmptyContentProperty);
        set => SetValue(EmptyContentProperty, value);
    }

    public string LoadingText
    {
        get => (string)GetValue(LoadingTextProperty);
        set => SetValue(LoadingTextProperty, value);
    }

    public bool ShowCompactToggle
    {
        get => (bool)GetValue(ShowCompactToggleProperty);
        set => SetValue(ShowCompactToggleProperty, value);
    }
}
