using System.Windows;
using System.Windows.Controls;
using MediaOrcestrator.Runner.Wpf.ViewModels;

namespace MediaOrcestrator.Runner.Wpf.Views.Controls;

public partial class StatusIndicator : UserControl
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status),
        typeof(TransferStatusKind),
        typeof(StatusIndicator),
        new PropertyMetadata(TransferStatusKind.Pending));

    public StatusIndicator()
    {
        InitializeComponent();
    }

    public TransferStatusKind Status
    {
        get => (TransferStatusKind)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }
}
