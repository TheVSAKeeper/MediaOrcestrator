namespace MediaOrcestrator.Domain.Migration;

public interface IWinFormsProcessDetector
{
    WinFormsProcessStatus CheckRunning(string sourceFolder, string databasePath);

    Task WaitForCloseAsync(
        string sourceFolder,
        string databasePath,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default);
}
