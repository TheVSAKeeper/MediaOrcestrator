using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.MediaContextMenu;
using MediaOrcestrator.Runner.Wpf.Services;
using System.Collections.ObjectModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MediaGridViewModel
{
    public void BuildContextMenu(MediaRowViewModel? triggerRow, Source? specificSource)
    {
        if (triggerRow is not null && !RowsView.Contains(triggerRow))
        {
            ContextMenuItems = [];
            OnPropertyChanged(nameof(ContextMenuItems));
            return;
        }

        if (triggerRow is not null && !_selectionOrder.Contains(triggerRow))
        {
            RequestSelection([triggerRow]);
            ScheduleSelectionUpdate();
        }

        ContextMenuItems = BuildMenu(GetSelectedMedias(), specificSource, includeAsync: true);
        OnPropertyChanged(nameof(ContextMenuItems));
    }

    private void BuildPanelActions()
    {
        PanelActionItems = BuildMenu(GetSelectedMedias(), null, includeAsync: false);

        var primary = PanelActionItems
            .Where(i => !i.IsSeparator && (i.HasChildren || i.Command is not null) && i.Icon != PackIconLucideKind.Trash2)
            .ToList();

        PrimaryActionGroups = BuildActionGroups(primary);
        TransferMenuItems = BuildTransferMenu();

        OnPropertyChanged(nameof(PanelActionItems));
        OnPropertyChanged(nameof(HasPanelActions));
        OnPropertyChanged(nameof(PrimaryActionGroups));
        OnPropertyChanged(nameof(HasPrimaryActions));
        OnPropertyChanged(nameof(TransferMenuItems));
    }

    private ObservableCollection<MediaMenuItemViewModel> BuildTransferMenu()
    {
        var items = new ObservableCollection<MediaMenuItemViewModel>();

        foreach (var item in PanelActionItems.Where(i => i.IsTransferAction))
        {
            items.Add(new()
            {
                Text = item.Text.StartsWith(SyncMenuTextPrefix, StringComparison.Ordinal)
                    ? item.Text[SyncMenuTextPrefix.Length..]
                    : item.Text,
                Icon = item.Icon,
                GlyphBrush = item.GlyphBrush,
                Tooltip = item.Tooltip,
                IsEnabled = item.IsEnabled,
                Command = item.Command,
            });
        }

        if (items.Count > 0)
        {
            items.Add(MediaMenuItemViewModel.Separator());
        }

        items.Add(new()
        {
            Text = "Создать связь",
            Icon = PackIconLucideKind.Plus,
            Tooltip = "Открыть раздел «Источники и связи»",
            Command = CreateRelationCommand,
        });

        return items;
    }

    private static IReadOnlyList<PrimaryActionGroup> BuildActionGroups(IReadOnlyList<MediaMenuItemViewModel> topLevel)
    {
        var groups = new List<PrimaryActionGroup>();

        foreach (var byIcon in topLevel.GroupBy(i => i.IconOrDefault))
        {
            var members = byIcon.ToList();
            var choices = new List<MediaMenuItemViewModel>();

            foreach (var member in members)
            {
                if (member is { HasChildren: true, Children: not null })
                {
                    choices.AddRange(member.Children.Where(c => !c.IsSeparator));
                }
                else
                {
                    choices.Add(member);
                }
            }

            if (choices.Count == 0)
            {
                continue;
            }

            groups.Add(new PrimaryActionGroup
            {
                Icon = byIcon.Key,
                Brush = members[0].GlyphBrush,
                Label = string.Join(" · ", members.Select(m => m.Text)),
                Choices = choices,
            });
        }

        return groups;
    }

    private ObservableCollection<MediaMenuItemViewModel> BuildMenu(List<Media> selected, Source? specificSource, bool includeAsync)
    {
        if (selected.Count == 0)
        {
            return [];
        }

        var selection = new MediaSelection(selected, specificSource)
        {
            InSelectionOrder = OrderBySelection(selected),
            GridSources = ColumnSources.ToList(),
        };

        var ctx = _actionContextFactory.Create(_actionUi);

        var revision = _selectionRevision;
        return _contextMenuBuilder.Build(selection, ctx, includeAsync, () => revision == _selectionRevision);
    }

    internal IReadOnlyList<Media> OrderBySelection(IReadOnlyList<Media> selected)
    {
        if (selected.Count < 2)
        {
            return selected;
        }

        var rank = new Dictionary<Media, int>(_selectionOrder.Count);

        for (var i = 0; i < _selectionOrder.Count; i++)
        {
            rank[_selectionOrder[i].Media] = i;
        }

        return selected
            .OrderBy(m => rank.TryGetValue(m, out var r) ? r : int.MaxValue)
            .ToList();
    }

    [RelayCommand]
    private void OpenInspector(MediaRowViewModel? row)
    {
        if (row is null || !Rows.Contains(row))
        {
            return;
        }

        if (!RowsView.Contains(row))
        {
            _revealedRow = row;
            RefreshFilteredRows();
        }

        RequestSelection([row]);
        RequestExpandInspector?.Invoke(this, EventArgs.Empty);
        UpdateSelectionState();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        RequestSelection([]);
        if (_revealedRow is not null)
        {
            _revealedRow = null;
            RefreshFilteredRows();
        }
        UpdateSelectionState();
    }

    private void RequestSelection(IEnumerable<MediaRowViewModel> rows)
    {
        _selectionOrder.Clear();
        _selectionOrder.AddRange(rows);
        NotifySelectionChanged();
        SelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NotifySelectionChanged()
    {
        _selectionRevision++;
        if (IsSelectionCurrent)
        {
            IsSelectionCurrent = false;
            NotifyCommandsChanged(PanelActionItems);
        }

        NotifyCommandsChanged(ContextMenuItems);
        SelectedCount = _selectionOrder.Count;
    }

    private static void NotifyCommandsChanged(IEnumerable<MediaMenuItemViewModel> items)
    {
        foreach (var item in items)
        {
            (item.Command as IRelayCommand)?.NotifyCanExecuteChanged();
            if (item.Children is { } children)
            {
                NotifyCommandsChanged(children);
            }
        }
    }

    [RelayCommand]
    private void CreateRelation()
    {
        _actionContextFactory.Create(_actionUi).Navigation.OpenSourcesGraph();
    }

    [RelayCommand]
    private void OpenRenameOperation() => OpenOperation(OperationsTab.Rename);

    [RelayCommand]
    private void OpenPreviewOperation() => OpenOperation(OperationsTab.Preview);

    [RelayCommand]
    private void OpenMergeOperation() => OpenOperation(OperationsTab.Merge);

    private void OpenOperation(OperationsTab tab)
    {
        var selected = _selectionOrder.Select(row => row.Media).ToList();

        if (selected.Count == 0)
        {
            return;
        }

        _actionContextFactory.Create(_actionUi).Navigation.OpenOperations(tab, selected);
    }

    private List<Media> GetSelectedMedias()
    {
        if (_selectionOrder.Count <= 1)
        {
            return _selectionOrder.Select(row => row.Media).ToList();
        }

        var selected = _selectionOrder.ToHashSet();
        return Rows.Where(selected.Contains).Select(row => row.Media).ToList();
    }

    private void UpdateSelectionState()
    {
        _selectionDebounceTimer.Stop();
        var selected = _selectionOrder;

        if (selected.Count == 1)
        {
            Focused = selected[0];
            _inspector.ShowSingle(selected[0].Media, _allSourcesProvider());
            MultiSourceTallies.Clear();
            MultiStatusBreakdown.Clear();
        }
        else if (selected.Count >= 2)
        {
            Focused = null;
            _inspector.ShowMulti(selected.Count);
            BuildMultiAggregates(selected);
        }
        else
        {
            Focused = null;
            _inspector.Clear();
            MultiSourceTallies.Clear();
            MultiStatusBreakdown.Clear();
            MultiSummaryText = string.Empty;
        }

        OnPropertyChanged(nameof(IsInspectorOpen));
        BuildPanelActions();
        IsSelectionCurrent = true;
    }

    private void BuildMultiAggregates(IReadOnlyList<MediaRowViewModel> selected)
    {
        var sourceTallies = new List<MultiSourceTally>(ColumnSources.Count);

        for (var i = 0; i < ColumnSources.Count; i++)
        {
            var present = 0;
            var errors = 0;

            foreach (var row in selected)
            {
                if (i >= row.Cells.Count)
                {
                    continue;
                }

                var cell = row.Cells[i];

                if (!cell.HasLink)
                {
                    continue;
                }

                present++;

                if (cell.Status?.Id == MediaStatus.Error)
                {
                    errors++;
                }
            }

            sourceTallies.Add(new(ColumnSources[i].Title, present, selected.Count, errors));
        }

        MultiSourceTallies.ReplaceAll(sourceTallies);

        var allCells = selected.SelectMany(r => r.Cells).ToList();

        var breakdown = allCells
            .Where(c => c.HasLink)
            .GroupBy(c => c.Status?.Id ?? "?")
            .Select(g =>
            {
                var first = g.First();

                return new MediaStatusTally(
                    first.Status?.Id ?? string.Empty,
                    first.Brush,
                    g.Count(),
                    first.Status?.Text ?? "–",
                    MediaStatusTally.OrderOf(first.Status?.Id));
            })
            .OrderBy(t => t.Order)
            .ToList();

        var missing = allCells.Count(c => !c.HasLink);

        if (missing > 0)
        {
            breakdown.Add(MediaStatusTally.Missing(missing));
        }

        MultiStatusBreakdown.ReplaceAll(breakdown);

        var fully = selected.Count(r => r.IsFullySynced);
        var withErrors = selected.Count(r => r.ErrorCount > 0);

        MultiSummaryText = withErrors > 0
            ? $"Полностью синхронизированы: {fully} из {selected.Count} · с ошибками: {withErrors}"
            : $"Полностью синхронизированы: {fully} из {selected.Count}";
    }

    [RelayCommand]
    private void FocusStatusFilter(string? statusId)
    {
        if (string.IsNullOrEmpty(statusId))
        {
            return;
        }

        _filter.FocusStatus(statusId);
    }
}
