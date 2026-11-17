using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class PublishView : UserControl, IView<PublishViewModel>
{
    private const double StackedBreakpoint = 820;

    private bool _isStacked;

    public PublishView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveLayout();

        if (DataContext is PublishViewModel vm)
        {
            vm.Reload();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (ActualWidth <= 0)
        {
            return;
        }

        var stacked = ActualWidth < StackedBreakpoint;

        if (stacked == _isStacked)
        {
            return;
        }

        _isStacked = stacked;

        if (stacked)
        {
            Grid.SetColumn(PreviewCard, 0);
            Grid.SetRow(PreviewCard, 1);
            PreviewColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(0));
            PreviewCard.SetCurrentValue(WidthProperty, double.NaN);
            PreviewCard.SetCurrentValue(MaxWidthProperty, 480d);
            PreviewCard.SetCurrentValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            PreviewCard.SetCurrentValue(MarginProperty, new Thickness(0, 16, 0, 0));
            OuterScrollViewer.SetCurrentValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            FormScrollViewer.SetCurrentValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        }
        else
        {
            Grid.SetColumn(PreviewCard, 1);
            Grid.SetRow(PreviewCard, 0);
            PreviewColumn.SetCurrentValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            PreviewCard.SetCurrentValue(WidthProperty, 340d);
            PreviewCard.SetCurrentValue(MaxWidthProperty, double.PositiveInfinity);
            PreviewCard.SetCurrentValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
            PreviewCard.SetCurrentValue(MarginProperty, new Thickness(16, 0, 0, 0));
            OuterScrollViewer.SetCurrentValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            FormScrollViewer.SetCurrentValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        }
    }

    private void OnDescriptionTemplateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo)
        {
            return;
        }

        if (combo.SelectedItem is not DescriptionTemplateViewModel template
            || DataContext is not PublishViewModel vm)
        {
            return;
        }

        vm.ApplyDescriptionTemplateCommand.Execute(template);
        combo.SelectedIndex = -1;
    }
}
