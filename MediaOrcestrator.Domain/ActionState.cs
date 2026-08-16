namespace MediaOrcestrator.Domain;

public enum ActionState
{
    None = 0,
    Pending = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}
