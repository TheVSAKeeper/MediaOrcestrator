using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MediaOrcestrator.Runner.Wpf.Views.Dialogs;

public partial class DocumentationDialogView : UserControl, IView<DocumentationDialogViewModel>
{
    public DocumentationDialogView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is DocumentationDialogViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnVmPropertyChanged;
        }

        if (e.NewValue is DocumentationDialogViewModel newVm)
        {
            newVm.PropertyChanged += OnVmPropertyChanged;
            RenderContent(newVm.Content, newVm.OpenExternalLink);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentationDialogViewModel.Content) && sender is DocumentationDialogViewModel vm)
        {
            RenderContent(vm.Content, vm.OpenExternalLink);
        }
    }

    private void RenderContent(string markdown, Action<Uri> openLink)
    {
        var doc = new FlowDocument
        {
            PagePadding = new System.Windows.Thickness(0),
            FontFamily = (System.Windows.Media.FontFamily?)TryFindResource("Font.Family.Body")
                         ?? new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 14,
        };

        foreach (var block in MarkdownRenderer.Render(markdown, openLink))
        {
            doc.Blocks.Add(block);
        }

        DocViewer.SetCurrentValue(System.Windows.Controls.FlowDocumentScrollViewer.DocumentProperty, doc);
    }
}
