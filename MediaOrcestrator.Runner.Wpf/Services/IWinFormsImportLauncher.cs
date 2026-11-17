namespace MediaOrcestrator.Runner.Wpf.Services;

public interface IWinFormsImportLauncher
{
    Task OpenAsync(bool isFirstRunPrompt = false);
}
