using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Animation;

namespace MediaOrcestrator.Runner.Wpf.Views.Media;

public partial class MediaInspectorSingleContent : UserControl
{
    public MediaInspectorSingleContent()
    {
        InitializeComponent();
    }

    private void OnPreviewImageTargetUpdated(object? sender, DataTransferEventArgs e)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        if (!SystemParameters.ClientAreaAnimation)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.SetCurrentValue(OpacityProperty, 1d);
            return;
        }

        var animation = new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(250));
        element.BeginAnimation(OpacityProperty, animation);
    }
}
