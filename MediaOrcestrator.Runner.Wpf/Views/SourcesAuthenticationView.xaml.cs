using System.Windows.Controls;
using MediaOrcestrator.Runner.Wpf.ViewModels;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class SourcesAuthenticationView : UserControl, IView<SourcesAuthenticationViewModel>
{
    private static readonly System.Windows.DependencyPropertyKey IsSummaryCompactPropertyKey =
        System.Windows.DependencyProperty.RegisterReadOnly(nameof(IsSummaryCompact), typeof(bool),
            typeof(SourcesAuthenticationView), new System.Windows.PropertyMetadata(false));

    public static readonly System.Windows.DependencyProperty IsSummaryCompactProperty = IsSummaryCompactPropertyKey.DependencyProperty;

    public bool IsSummaryCompact => (bool)GetValue(IsSummaryCompactProperty);

    public SourcesAuthenticationView()
    {
        InitializeComponent();
        SizeChanged += (_, args) => SetValue(IsSummaryCompactPropertyKey, args.NewSize.Height < 420);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: { } selected } list && e.OriginalSource == list)
        {
            list.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (ReferenceEquals(list.SelectedItem, selected))
                {
                    list.ScrollIntoView(selected);
                }
            }));
        }
    }
}
