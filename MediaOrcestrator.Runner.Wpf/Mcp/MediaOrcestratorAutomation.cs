using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;

namespace MediaOrcestrator.Runner.Wpf.Mcp;

public sealed class MediaOrcestratorAutomation(ActionHolder actions, Orcestrator orcestrator, Paths paths)
{
    private const int CompletedTake = 10;

    public AppStateDto GetState()
    {
        var active = actions.Snapshot();
        var completed = actions.CompletedSnapshot();

        return new AppStateDto(
            App.ResolveAppVersion(),
            paths.DatabasePath,
            active.Any(action => action.Kind == ActionKind.Sync),
            ReadSources(),
            [.. active.Select(ToActiveDto)],
            [.. completed.Take(CompletedTake).Select(ToCompletedDto)]);
    }

    private IReadOnlyList<SourceStateDto> ReadSources()
    {
        try
        {
            return
            [
                .. orcestrator.GetSources()
                    .Select(source => new SourceStateDto(
                        source.Id,
                        source.Settings.TryGetValue("_system_name", out var name) && !string.IsNullOrWhiteSpace(name)
                            ? name
                            : source.TypeId,
                        source.TypeId,
                        source.IsDisable,
                        source.LastSyncedAt)),
            ];
        }
        catch (Exception exception)
        {
            return [new SourceStateDto(string.Empty, $"Список источников не прочитан: {exception.Message}", string.Empty, true, null)];
        }
    }

    private static ActiveActionDto ToActiveDto(ActionHolder.RunningAction action)
    {
        return new ActiveActionDto(
            action.Name,
            string.IsNullOrWhiteSpace(action.Subtitle) ? null : action.Subtitle,
            action.Kind,
            action.State,
            action.Status,
            action.ProgressValue,
            action.ProgressMax,
            action.Depth,
            action.StartedAt);
    }

    private static CompletedActionDto ToCompletedDto(ActionHolder.RunningAction action)
    {
        return new CompletedActionDto(
            action.Name,
            action.State,
            action.Duration,
            FirstLine(action.Error));
    }

    private static string? FirstLine(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        var breakAt = error.IndexOfAny(['\r', '\n']);

        return breakAt < 0 ? error : error[..breakAt];
    }
}

public sealed record AppStateDto(
    string Version,
    string DatabasePath,
    bool SyncRunning,
    IReadOnlyList<SourceStateDto> Sources,
    IReadOnlyList<ActiveActionDto> ActiveActions,
    IReadOnlyList<CompletedActionDto> CompletedActions);

public sealed record SourceStateDto(string Id, string Name, string TypeId, bool IsDisabled, DateTime? LastSyncedAt);

public sealed record ActiveActionDto(
    string Name,
    string? Subtitle,
    ActionKind Kind,
    ActionState State,
    string Status,
    int ProgressValue,
    int ProgressMax,
    int Depth,
    DateTime StartedAt);

public sealed record CompletedActionDto(string Name, ActionState State, TimeSpan Duration, string? Error);
