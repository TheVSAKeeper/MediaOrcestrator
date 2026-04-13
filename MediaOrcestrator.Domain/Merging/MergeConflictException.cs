namespace MediaOrcestrator.Domain.Merging;

public sealed class MergeConflictException(IReadOnlyList<string> conflicts)
    : InvalidOperationException(BuildMessage(conflicts))
{
    public IReadOnlyList<string> Conflicts { get; } = conflicts;

    private static string BuildMessage(IReadOnlyList<string> conflicts)
    {
        return conflicts.Count == 0
            ? "Объединение содержит конфликты источников"
            : $"Объединение содержит конфликты источников:\n{string.Join("\n", conflicts)}";
    }
}
