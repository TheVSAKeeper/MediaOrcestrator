using MediaOrcestrator.Runner.Wpf.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MediaOrcestrator.Runner.Wpf.Views.Dialogs;

public partial class CoverTemplateEditorView : UserControl, IView<CoverTemplateEditorViewModel>
{
    public CoverTemplateEditorView()
    {
        InitializeComponent();
        Loaded += (_, _) => InspectorPanel.FocusProfileName();
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        UpdateLayerPosition(sender, e.GetPosition);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateLayerPosition(sender, e.GetPosition);
        }
    }

    private void UpdateLayerPosition(object sender, Func<IInputElement, Point> getPosition)
    {
        if (sender is not FrameworkElement element
            || DataContext is not CoverTemplateEditorViewModel viewModel
            || viewModel.PreviewImage is not BitmapSource preview
            || viewModel.SelectedLayer is null
            || element.ActualWidth <= 0
            || element.ActualHeight <= 0
            || preview.PixelWidth <= 0
            || preview.PixelHeight <= 0)
        {
            return;
        }

        var point = getPosition(element);

        var scale = Math.Min(
            element.ActualWidth / preview.PixelWidth,
            element.ActualHeight / preview.PixelHeight);
        var renderedWidth = preview.PixelWidth * scale;
        var renderedHeight = preview.PixelHeight * scale;
        var offsetX = (element.ActualWidth - renderedWidth) / 2;
        var offsetY = (element.ActualHeight - renderedHeight) / 2;

        viewModel.SetSelectedLayerPosition(
            (point.X - offsetX) / renderedWidth * 100,
            (point.Y - offsetY) / renderedHeight * 100);
    }
}
