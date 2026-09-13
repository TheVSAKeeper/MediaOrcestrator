namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public enum TransferJournalLevel
{
    None = 0,
    Info = 1,
    Success = 2,
    Warning = 3,
    Error = 4,
}

public sealed record TransferJournalEntry(DateTime When, TransferJournalLevel Level, string Text)
{
    public string TimeText => When.ToString("HH:mm:ss");
}
