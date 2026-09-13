using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace MediaOrcestrator.Runner.Wpf.Views.Controls;

[ContentProperty(nameof(LeftContent))]
public partial class OperationActionBar : UserControl
{
    public static readonly DependencyProperty LeftContentProperty = DependencyProperty.Register(
        nameof(LeftContent),
        typeof(object),
        typeof(OperationActionBar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ToggleJournalCommandProperty = DependencyProperty.Register(
        nameof(ToggleJournalCommand),
        typeof(ICommand),
        typeof(OperationActionBar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ApplyTextProperty = DependencyProperty.Register(
        nameof(ApplyText),
        typeof(string),
        typeof(OperationActionBar),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ApplyCommandProperty = DependencyProperty.Register(
        nameof(ApplyCommand),
        typeof(ICommand),
        typeof(OperationActionBar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.Register(
        nameof(CancelCommand),
        typeof(ICommand),
        typeof(OperationActionBar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy),
        typeof(bool),
        typeof(OperationActionBar),
        new PropertyMetadata(false));

    public static readonly DependencyProperty ProgressValueProperty = DependencyProperty.Register(
        nameof(ProgressValue),
        typeof(double),
        typeof(OperationActionBar),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty ProgressMaxProperty = DependencyProperty.Register(
        nameof(ProgressMax),
        typeof(double),
        typeof(OperationActionBar),
        new PropertyMetadata(1d));

    public OperationActionBar()
    {
        InitializeComponent();
    }

    public object? LeftContent
    {
        get => GetValue(LeftContentProperty);
        set => SetValue(LeftContentProperty, value);
    }

    public ICommand? ToggleJournalCommand
    {
        get => (ICommand?)GetValue(ToggleJournalCommandProperty);
        set => SetValue(ToggleJournalCommandProperty, value);
    }

    public string ApplyText
    {
        get => (string)GetValue(ApplyTextProperty);
        set => SetValue(ApplyTextProperty, value);
    }

    public ICommand? ApplyCommand
    {
        get => (ICommand?)GetValue(ApplyCommandProperty);
        set => SetValue(ApplyCommandProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => (ICommand?)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public double ProgressValue
    {
        get => (double)GetValue(ProgressValueProperty);
        set => SetValue(ProgressValueProperty, value);
    }

    public double ProgressMax
    {
        get => (double)GetValue(ProgressMaxProperty);
        set => SetValue(ProgressMaxProperty, value);
    }
}
