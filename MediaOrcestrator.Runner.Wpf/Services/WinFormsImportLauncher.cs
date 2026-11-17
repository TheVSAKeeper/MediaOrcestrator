using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MediaOrcestrator.Runner.Wpf.Services;

internal sealed class WinFormsImportLauncher(
    IServiceProvider services,
    IDialogService dialogs) : IWinFormsImportLauncher
{
    public Task OpenAsync(bool isFirstRunPrompt = false)
    {
        var viewModel = services.GetRequiredService<WinFormsImportDialogViewModel>();
        viewModel.Configure(isFirstRunPrompt);
        return dialogs.ShowAsync(viewModel);
    }
}
