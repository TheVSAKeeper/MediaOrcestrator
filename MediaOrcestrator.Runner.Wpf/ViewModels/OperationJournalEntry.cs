namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public enum OperationJournalLevel
{
    None = 0,
    Info = 1,
    Success = 2,
    Warning = 3,
    Error = 4,
}

public sealed record OperationJournalEntry(DateTime Timestamp, OperationJournalLevel Level, string Text, int Indent = 0)
{
    public string TimestampText => Timestamp.ToString("HH:mm:ss");
}
