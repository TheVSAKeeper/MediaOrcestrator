using KeepShell.Diagnostics;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaOrcestrator.Runner.Wpf.Services.Diagnostics;

public sealed class MediaOrcestratorBundleSource(
    Paths paths,
    DatabaseMaintenanceService maintenance,
    DatabaseBackupService backups,
    PluginManager plugins,
    ToolManager tools,
    Orcestrator orcestrator,
    ActionHolder actionHolder) : IDiagnosticsBundleSource
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public IEnumerable<DiagnosticsEntry> Collect()
    {
        var overview = new
        {
            CapturedAtUtc = DateTime.UtcNow,
            Paths = new
            {
                paths.DatabasePath,
                paths.TempPath,
                paths.StatePath,
                paths.PluginPath,
                maintenance.BackupsPath,
                maintenance.LogsPath,
            },
            Database = new
            {
                SizeBytes = maintenance.MeasureDatabaseFile(),
                PendingRestore = backups.GetPendingRestore(),
                BackupsCount = backups.GetBackups().Count,
                Collections = maintenance.KnownCollections.ToDictionary(
                    name => name,
                    name => maintenance.CountCollection(name)),
            },
            Plugins = plugins.MediaSources.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            Tools = tools.GetRegistry().Keys.OrderBy(k => k, StringComparer.Ordinal)
                .Select(name => new
                {
                    Name = name,
                    ResolvedPath = tools.GetToolPath(name),
                })
                .ToArray(),
        };

        var sources = orcestrator.GetSources()
            .Select(s => new
            {
                s.Id,
                s.TypeId,
                s.Title,
                s.IsDisable,
                s.LastSyncedAt,
                SettingKeys = s.Settings.Keys
                    .Where(k => !k.StartsWith("_system", StringComparison.Ordinal))
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToArray(),
            })
            .ToArray();

        var relationsCount = orcestrator.GetRelations().Count;

        var activeTasks = actionHolder.Snapshot();
        var completedTasks = actionHolder.CompletedSnapshot();

        var tasks = new
        {
            ActiveCount = activeTasks.Count,
            Active = activeTasks.Select(FormatAction).ToArray(),
            RecentCompleted = completedTasks.Take(20).Select(FormatAction).ToArray(),
        };

        yield return new("mediaorcestrator/overview.json", JsonSerializer.Serialize(overview, Json));
        yield return new("mediaorcestrator/sources.json", JsonSerializer.Serialize(new
        {
            Sources = sources,
            RelationsCount = relationsCount,
        }, Json));
        yield return new("mediaorcestrator/tasks.json", JsonSerializer.Serialize(tasks, Json));
    }

    private static object FormatAction(ActionHolder.RunningAction action) => new
    {
        action.Id,
        action.Name,
        Kind = action.Kind.ToString(),
        State = action.State.ToString(),
        action.Status,
        action.ProgressValue,
        action.ProgressMax,
        action.StartedAt,
        action.FinishedAt,
        action.Error,
    };
}
