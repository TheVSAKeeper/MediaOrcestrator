using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using System.Text;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class TasksDebugViewModel : ObservableObject, IDialogViewModel
{
    private readonly ActionHolder _actionHolder;
    private readonly IClipboardService _clipboard;

    [ObservableProperty]
    private string _dump = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    public TasksDebugViewModel(ActionHolder actionHolder, IClipboardService clipboard)
    {
        _actionHolder = actionHolder;
        _clipboard = clipboard;
        Refresh();
    }

    public event EventHandler<bool>? RequestClose;

    [RelayCommand]
    private void Refresh()
    {
        var active = _actionHolder.Snapshot();
        var completed = _actionHolder.CompletedSnapshot();

        Summary = $"Активных: {active.Count} · Завершённых: {completed.Count} (ретенция 50) · снимок {DateTime.Now:HH:mm:ss}";

        var sb = new StringBuilder();

        sb.Append("━━━ Активные (").Append(active.Count).AppendLine(") ━━━");

        if (active.Count == 0)
        {
            sb.AppendLine("  нет активных задач");
        }
        else
        {
            foreach (var action in active)
            {
                AppendAction(sb, action);
            }
        }

        sb.AppendLine();
        sb.Append("━━━ Завершённые (").Append(completed.Count).AppendLine(") ━━━");

        if (completed.Count == 0)
        {
            sb.AppendLine("  история пуста");
        }
        else
        {
            foreach (var action in completed)
            {
                AppendAction(sb, action);
            }
        }

        Dump = sb.ToString();
    }

    [RelayCommand]
    private void CopyDump() => _clipboard.TrySetText(Dump);

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke(this, false);
    }

    private static void AppendAction(StringBuilder sb, ActionHolder.RunningAction action)
    {
        var indent = new string(' ', Math.Max(action.Depth, 0) * 4);

        sb.Append(indent)
            .Append('[').Append(action.State).Append("] ")
            .Append(action.Name)
            .Append(" – ").Append(action.Kind)
            .AppendLine();

        AppendField(sb, indent, "Id", action.Id.ToString("D"));
        AppendField(sb, indent, "Parent", action.ParentId is { } parent ? parent.ToString("D") : "–");
        AppendField(sb, indent, "Depth", action.Depth.ToString());
        AppendField(sb, indent, "Status", string.IsNullOrEmpty(action.Status) ? "–" : action.Status);

        if (!string.IsNullOrEmpty(action.Subtitle))
        {
            AppendField(sb, indent, "Subtitle", action.Subtitle);
        }

        AppendField(sb, indent, "Progress", FormatProgress(action));
        AppendField(sb, indent, "Started", action.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        AppendField(sb, indent, "Finished", action.FinishedAt is { } finished ? finished.ToString("yyyy-MM-dd HH:mm:ss") : "–");
        AppendField(sb, indent, "Duration", FormatDuration(action.Duration));

        if (!string.IsNullOrEmpty(action.Error))
        {
            AppendField(sb, indent, "Error", action.Error);
        }

        sb.AppendLine();
    }

    private static void AppendField(StringBuilder sb, string indent, string label, string value)
    {
        sb.Append(indent).Append("    ").Append(label.PadRight(9)).Append(": ").AppendLine(value);
    }

    private static string FormatProgress(ActionHolder.RunningAction action)
    {
        var max = action.ProgressMax;

        return max > 0
            ? $"{action.ProgressValue} / {max}"
            : action.ProgressValue > 0 ? $"{action.ProgressValue} (неопределённый)" : "неопределённый";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration < TimeSpan.FromHours(1)
            ? duration.ToString(@"mm\:ss\.fff")
            : duration.ToString(@"hh\:mm\:ss");
    }
}
