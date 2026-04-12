namespace MediaOrcestrator.Domain;

public sealed record SyncAttemptStatus(
    SyncAttemptKind Kind,
    int AttemptNumber,
    int MaxAttempts,
    TimeSpan? NextDelay = null,
    DateTimeOffset? NextAttemptAt = null,
    Exception? Error = null);
