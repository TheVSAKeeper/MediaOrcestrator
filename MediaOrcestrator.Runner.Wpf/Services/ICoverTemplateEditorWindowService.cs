using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.ViewModels;

namespace MediaOrcestrator.Runner.Wpf.Services;

public interface ICoverTemplateEditorWindowService
{
    void Open(CoverTemplate? initial, string? profileName, string? sampleTitle, CoverProfilePickerViewModel? caller = null);

    bool IsActiveCaller(CoverProfilePickerViewModel picker);
}
