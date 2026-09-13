namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public enum TransferStatusKind
{
    None = 0,
    Pending = 1,
    Working = 2,
    Waiting = 3,
    Ok = 4,
    Error = 5,
    NonRetriable = 6,
    Joined = 7,
}
