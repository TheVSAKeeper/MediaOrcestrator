using KeepShell.ViewModels;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using Microsoft.Extensions.DependencyInjection;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap.Gallery;

internal static partial class GalleryStates
{
    public const string Idle = "idle";
    public const string Empty = "empty";
    public const string Busy = "busy";
    public const string Error = "error";

    private const int BusyValue = 42;
    private const int BusyMax = 100;
    private const string EmptyFilterNeedle = "__gallery_empty__";

    private static readonly string[] EmptyPages =
    [
        "media",
        "sources",
        "audit",
        "transfers",
        "chronology",
        "rename",
        "preview",
        "merge",
        "conflicts",
        "comments",
        "tasks",
        "logs",
    ];

    private static readonly string[] BusyPages =
    [
        "media",
        "audit",
        "transfers",
        "publish",
        "rename",
        "preview",
        "merge",
        "conflicts",
        "comments",
        "tools",
        "cleanup",
        "tasks",
    ];

    private static readonly string[] ErrorPages =
    [
        "media",
        "audit",
        "transfers",
        "publish",
        "comments",
        "cleanup",
        "tasks",
    ];

    private static ActionHolder.RunningAction? _activeAction;
    private static CancellationTokenSource? _activeCts;
    private static bool _savedTasksCompletedExpanded;

    public static IReadOnlyList<string> All { get; } = [Idle, Empty, Busy, Error];

    public static string? Match(string state)
    {
        return All.FirstOrDefault(known => string.Equals(known, state, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsIdle(string state)
    {
        return string.Equals(state, Idle, StringComparison.Ordinal);
    }

    public static bool SupportsPage(string page, string state)
    {
        return state switch
        {
            Empty => EmptyPages.Contains(page, StringComparer.Ordinal),
            Busy => BusyPages.Contains(page, StringComparer.Ordinal),
            Error => ErrorPages.Contains(page, StringComparer.Ordinal),
            _ => true,
        };
    }

    public static bool SupportsDialog(string dialog, string state)
    {
        return IsIdle(state);
    }

    public static Task ApplyPageAsync(string page, string state, IServiceProvider services)
    {
        if (IsIdle(state))
        {
            return Task.CompletedTask;
        }

        switch (state)
        {
            case Empty:
                ApplyEmpty(page, services);
                break;

            case Busy:
                ApplyBusy(page, services);
                break;

            case Error:
                ApplyError(page, services);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Неизвестное состояние галереи.");
        }

        return Task.CompletedTask;
    }

    public static void ResetPage(string page, string state, IServiceProvider services)
    {
        if (IsIdle(state))
        {
            return;
        }

        ClearSyntheticAction(services);

        switch (state)
        {
            case Empty:
                ResetEmpty(page, services);
                break;

            case Busy:
                ResetBusy(page, services);
                break;

            case Error:
                ResetError(page, services);
                break;
        }
    }

    private static void ApplyEmpty(string page, IServiceProvider services)
    {
        switch (page)
        {
            case "media":
                var media = services.GetRequiredService<MediaViewModel>();
                media.Filter.SetSearchImmediateForAutomation(EmptyFilterNeedle);
                media.Grid.RefreshViewForAutomation();
                break;

            case "sources":
                services.GetRequiredService<SourcesRelationsViewModel>().SetEmptyForAutomation(true);
                break;

            case "audit":
                services.GetRequiredService<AuditViewModel>().SetEmptyForAutomation(true);
                break;

            case "transfers":
                services.GetRequiredService<TransfersViewModel>().SetSearchImmediateForAutomation(EmptyFilterNeedle);
                break;

            case "chronology":
                var chronology = services.GetRequiredService<ChronologyViewModel>();
                chronology.Items.Clear();
                chronology.HasResults = false;
                break;

            case "rename":
                services.GetRequiredService<BatchRenameSubViewModel>().ClearMediaCommand.Execute(null);
                break;

            case "preview":
                services.GetRequiredService<BatchPreviewSubViewModel>().ClearMediaCommand.Execute(null);
                break;

            case "merge":
                services.GetRequiredService<MergeDuplicatesSubViewModel>().SetEmptyForAutomation(true);
                break;

            case "conflicts":
                services.GetRequiredService<ResolveConflictsSubViewModel>().SetEmptyForAutomation(true);
                break;

            case "comments":
                services.GetRequiredService<CommentsViewModel>().SetEmptyForAutomation(true);
                break;

            case "tasks":
                services.GetRequiredService<ActionHolder>().ClearCompleted();
                break;

            case "logs":
                services.GetRequiredService<LogsViewModel>().SearchText = EmptyFilterNeedle;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(page), page, "У этой страницы нет пустого состояния.");
        }
    }

    private static void ResetEmpty(string page, IServiceProvider services)
    {
        switch (page)
        {
            case "media":
                var media = services.GetRequiredService<MediaViewModel>();
                media.Filter.SetSearchImmediateForAutomation(string.Empty);
                media.Grid.RefreshViewForAutomation();
                break;

            case "sources":
                services.GetRequiredService<SourcesRelationsViewModel>().SetEmptyForAutomation(false);
                break;

            case "audit":
                services.GetRequiredService<AuditViewModel>().SetEmptyForAutomation(false);
                break;

            case "transfers":
                services.GetRequiredService<TransfersViewModel>().SetSearchImmediateForAutomation(string.Empty);
                break;

            case "merge":
                services.GetRequiredService<MergeDuplicatesSubViewModel>().SetEmptyForAutomation(false);
                break;

            case "conflicts":
                services.GetRequiredService<ResolveConflictsSubViewModel>().SetEmptyForAutomation(false);
                break;

            case "comments":
                services.GetRequiredService<CommentsViewModel>().SetEmptyForAutomation(false);
                break;

            case "logs":
                services.GetRequiredService<LogsViewModel>().SearchText = string.Empty;
                break;
        }
    }
}
