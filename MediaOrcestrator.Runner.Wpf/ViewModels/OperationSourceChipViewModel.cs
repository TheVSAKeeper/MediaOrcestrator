namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public enum OperationSourceChipState
{
    None = 0,

    Synced = 1,

    OutOfSync = 2,

    Skipped = 3,
}

public sealed class OperationSourceChipViewModel
{
    public required string Text { get; init; }

    public required OperationSourceChipState State { get; init; }

    public string? Tooltip { get; init; }
}
