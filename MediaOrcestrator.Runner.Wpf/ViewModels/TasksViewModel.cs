using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Shell;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class TasksViewModel : ObservableObject
{
    private const string CompletedExpandedKey = "wpf.tasks.completed_expanded";

    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly Dictionary<Guid, ActionRowViewModel> _activeRows = [];
    private readonly Dictionary<Guid, ActionRowViewModel> _completedRows = [];
    private bool _suppressPersist;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActive))]
    [NotifyPropertyChangedFor(nameof(HasNoActive))]
    [NotifyPropertyChangedFor(nameof(HasAnyTasks))]
    [NotifyPropertyChangedFor(nameof(HasVisibleItems))]
    [NotifyPropertyChangedFor(nameof(HasNoVisibleItems))]
    [NotifyPropertyChangedFor(nameof(Header))]
    private int _activeCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompleted))]
    [NotifyPropertyChangedFor(nameof(HasAnyTasks))]
    [NotifyPropertyChangedFor(nameof(HasVisibleItems))]
    [NotifyPropertyChangedFor(nameof(HasNoVisibleItems))]
    [NotifyPropertyChangedFor(nameof(CompletedHeader))]
    private int _completedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    private int _errorCount;

    [ObservableProperty]
    private TaskbarItemProgressState _taskbarState = TaskbarItemProgressState.None;

    [ObservableProperty]
    private double _taskbarValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompletedHeader))]
    [NotifyPropertyChangedFor(nameof(CompletedChevron))]
    [NotifyPropertyChangedFor(nameof(CompletedExpandedVisibility))]
    [NotifyPropertyChangedFor(nameof(HasVisibleItems))]
    [NotifyPropertyChangedFor(nameof(HasNoVisibleItems))]
    private bool _isCompletedExpanded;

    public TasksViewModel(ActionHolder actionHolder, IDialogService dialogs, ISettingsStore settings)
    {
        _actionHolder = actionHolder;
        _dialogs = dialogs;
        _settings = settings;
        _actionHolder.Changed += OnActionsChanged;
        Active = [];
        Completed = [];

        _suppressPersist = true;
        IsCompletedExpanded = bool.TryParse(_settings.GetStringValue(CompletedExpandedKey), out var expanded) && expanded;
        _suppressPersist = false;

        Rebuild();
    }

    public ObservableCollection<ActionRowViewModel> Active { get; }

    public ObservableCollection<ActionRowViewModel> Completed { get; }

    public RangeObservableCollection<object> VisibleRows { get; } = [];

    public bool HasActive => ActiveCount > 0;

    public bool HasNoActive => ActiveCount == 0;

    public bool HasCompleted => CompletedCount > 0;

    public bool HasAnyTasks => ActiveCount > 0 || CompletedCount > 0;

    public bool HasVisibleItems => ActiveCount > 0 || (CompletedCount > 0 && IsCompletedExpanded);

    public bool HasNoVisibleItems => !HasVisibleItems;

    public bool HasErrors => ErrorCount > 0;

    public string Header => ActiveCount == 0 ? "Активных задач нет" : $"Активных задач: {ActiveCount}";

    public string CompletedHeader => $"Завершённые ({CompletedCount})";

    public PackIconLucideKind CompletedChevron =>
        IsCompletedExpanded ? PackIconLucideKind.ChevronDown : PackIconLucideKind.ChevronRight;

    public Visibility CompletedExpandedVisibility =>
        IsCompletedExpanded ? Visibility.Visible : Visibility.Collapsed;

    [RelayCommand]
    private void CancelAll()
    {
        var snapshot = _actionHolder.Snapshot();

        if (snapshot.Count == 0)
        {
            return;
        }

        if (!_dialogs.ConfirmWarning("Отмена задач",
                $"Отменить все активные задачи ({snapshot.Count})?"))
        {
            return;
        }

        foreach (var action in snapshot)
        {
            action.Cancel();
        }
    }

    [RelayCommand]
    private void ClearCompleted()
    {
        _actionHolder.ClearCompleted();
    }

    [RelayCommand]
    private async Task ShowDebugDump()
    {
        await _dialogs.ShowAsync(new TasksDebugViewModel(_actionHolder));
    }

    partial void OnIsCompletedExpandedChanged(bool value)
    {
        RebuildVisibleRows();

        if (_suppressPersist)
        {
            return;
        }

        _settings.SetValue(CompletedExpandedKey, value ? "true" : "false");
    }

    [RelayCommand]
    private void ShowError(ActionRowViewModel? row)
    {
        if (row is null || string.IsNullOrEmpty(row.Error))
        {
            return;
        }

        _dialogs.Error(row.Name, row.Error);
    }

    private void OnActionsChanged(object? sender, EventArgs e)
    {
        var app = Application.Current;

        if (app is null)
        {
            return;
        }

        app.Dispatcher.BeginInvoke(Rebuild);
    }

    private void Rebuild()
    {
        var activeSnapshot = _actionHolder.Snapshot();
        var completedSnapshot = _actionHolder.CompletedSnapshot();

        Sync(activeSnapshot, Active, _activeRows);
        Sync(completedSnapshot, Completed, _completedRows);

        RefreshHierarchy(activeSnapshot, _activeRows);
        RefreshHierarchy(completedSnapshot, _completedRows);

        ActiveCount = Active.Count;
        CompletedCount = Completed.Count;
        RebuildVisibleRows();
        RecalculateAggregates();
    }

    private void Sync(
        IReadOnlyList<ActionHolder.RunningAction> source,
        ObservableCollection<ActionRowViewModel> target,
        Dictionary<Guid, ActionRowViewModel> map)
    {
        var present = new HashSet<Guid>(source.Count);

        for (var i = 0; i < source.Count; i++)
        {
            var action = source[i];
            present.Add(action.Id);

            if (!map.TryGetValue(action.Id, out var row))
            {
                row = new(action, MarkRowDirty, OnRowExpandedChanged);
                map[action.Id] = row;
                target.Insert(Math.Min(i, target.Count), row);
                continue;
            }

            var currentIndex = target.IndexOf(row);

            if (currentIndex != i && currentIndex >= 0)
            {
                target.Move(currentIndex, i);
            }
        }

        for (var i = target.Count - 1; i >= 0; i--)
        {
            var row = target[i];

            if (present.Contains(row.Id))
            {
                continue;
            }

            target.RemoveAt(i);
            map.Remove(row.Id);
            row.Dispose();
        }
    }

    private static void RefreshHierarchy(
        IReadOnlyList<ActionHolder.RunningAction> source,
        Dictionary<Guid, ActionRowViewModel> map)
    {
        var withChildren = new HashSet<Guid>();

        foreach (var action in source)
        {
            if (action.ParentId is { } parentId)
            {
                withChildren.Add(parentId);
            }
        }

        foreach (var action in source)
        {
            if (map.TryGetValue(action.Id, out var row))
            {
                row.HasChildren = withChildren.Contains(action.Id);
            }
        }

        int? collapsedAtDepth = null;

        foreach (var action in source)
        {
            if (!map.TryGetValue(action.Id, out var row))
            {
                continue;
            }

            if (collapsedAtDepth.HasValue && action.Depth <= collapsedAtDepth.Value)
            {
                collapsedAtDepth = null;
            }

            row.IsVisible = collapsedAtDepth is null;

            if (row.IsVisible && row.HasChildren && !row.IsExpanded)
            {
                collapsedAtDepth = action.Depth;
            }
        }
    }

    private void OnRowExpandedChanged(ActionRowViewModel _)
    {
        RefreshHierarchy(_actionHolder.Snapshot(), _activeRows);
        RefreshHierarchy(_actionHolder.CompletedSnapshot(), _completedRows);
        RebuildVisibleRows();
    }

    private void RebuildVisibleRows()
    {
        var items = new List<object>(Active.Count + (IsCompletedExpanded ? Completed.Count + 1 : 0));

        foreach (var row in Active)
        {
            if (row.IsVisible)
            {
                items.Add(row);
            }
        }

        if (IsCompletedExpanded)
        {
            var visibleCompleted = Completed.Where(r => r.IsVisible).ToList();

            if (items.Count > 0 && visibleCompleted.Count > 0)
            {
                items.Add(TasksSectionDivider.Instance);
            }

            items.AddRange(visibleCompleted);
        }

        VisibleRows.ReplaceAll(items);
    }

    private void MarkRowDirty()
    {
        OnPropertyChanged(nameof(Header));
        RecalculateAggregates();
    }

    private void RecalculateAggregates()
    {
        ErrorCount = CountFailed(_actionHolder.CompletedSnapshot());

        var active = _actionHolder.Snapshot();

        if (active.Count == 0)
        {
            TaskbarState = ErrorCount > 0 ? TaskbarItemProgressState.Error : TaskbarItemProgressState.None;
            TaskbarValue = 0;
            return;
        }

        var totalPercent = 0.0;
        var determinate = 0;

        foreach (var action in active)
        {
            var max = action.ProgressMax;

            if (max <= 0)
            {
                continue;
            }

            var value = Math.Clamp(action.ProgressValue, 0, max);
            totalPercent += value / (double)max;
            determinate++;
        }

        if (determinate == 0)
        {
            TaskbarState = TaskbarItemProgressState.Indeterminate;
            TaskbarValue = 0;
            return;
        }

        TaskbarState = TaskbarItemProgressState.Normal;
        TaskbarValue = totalPercent / determinate;
    }

    private static int CountFailed(IReadOnlyList<ActionHolder.RunningAction> completed)
    {
        var failed = 0;

        for (var i = 0; i < completed.Count; i++)
        {
            if (completed[i].State == ActionState.Failed)
            {
                failed++;
            }
        }

        return failed;
    }
}

public sealed class TasksSectionDivider
{
    public static TasksSectionDivider Instance { get; } = new();

    private TasksSectionDivider()
    {
    }
}
