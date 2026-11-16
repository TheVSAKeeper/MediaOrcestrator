using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using System.ComponentModel;
using System.Globalization;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class TransfersViewModel
{
    internal void OnNodeSelectionChanged(TransferIntentNodeViewModel node, bool value)
    {
        if (value)
        {
            var ancestor = node.Parent;

            while (ancestor != null)
            {
                ancestor.SetIsSelectedSilent(true);
                ancestor = ancestor.Parent;
            }
        }
        else
        {
            foreach (var child in node.Children)
            {
                ClearDescendantsSilent(child);
            }
        }

        FindGroupOf(node)?.Recompute();
        RecomputeCounts();
    }

    internal void OnGroupBulkSelection() => RecomputeCounts();

    internal void MoveGroup(TransferMediaGroupViewModel source, TransferMediaGroupViewModel target, bool insertBelow)
    {
        if (ReferenceEquals(source, target))
        {
            return;
        }

        var sourceIndex = Groups.IndexOf(source);
        var targetIndex = Groups.IndexOf(target);

        if (sourceIndex < 0 || targetIndex < 0)
        {
            return;
        }

        var insertIndex = insertBelow ? targetIndex + 1 : targetIndex;

        if (sourceIndex < insertIndex)
        {
            insertIndex--;
        }

        if (insertIndex == sourceIndex)
        {
            return;
        }

        Groups.Move(sourceIndex, insertIndex);
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TransferIntentNodeViewModel.IsSelected))
        {
            return;
        }

        ExecuteSelectedCommand.NotifyCanExecuteChanged();
    }

    private void AttachGroup(TransferMediaGroupViewModel group)
    {
        foreach (var child in group.Children)
        {
            AttachNodeRecursive(child);
        }
    }

    private void AttachNodeRecursive(TransferIntentNodeViewModel node)
    {
        node.PropertyChanged += OnNodePropertyChanged;

        foreach (var c in node.Children)
        {
            AttachNodeRecursive(c);
        }
    }

    private void DetachGroup(TransferMediaGroupViewModel group)
    {
        foreach (var child in group.Children)
        {
            DetachNodeRecursive(child);
        }
    }

    private void DetachNodeRecursive(TransferIntentNodeViewModel node)
    {
        node.PropertyChanged -= OnNodePropertyChanged;

        foreach (var c in node.Children)
        {
            DetachNodeRecursive(c);
        }
    }

    private static void ClearDescendantsSilent(TransferIntentNodeViewModel node)
    {
        node.SetIsSelectedSilent(false);

        foreach (var child in node.Children)
        {
            ClearDescendantsSilent(child);
        }
    }

    private TransferMediaGroupViewModel? FindGroupOf(TransferIntentNodeViewModel node)
    {
        var root = node;

        while (root.Parent != null)
        {
            root = root.Parent;
        }

        var mediaId = root.Intent.Media.Id;

        foreach (var group in Groups)
        {
            if (string.Equals(group.Media.Id, mediaId, StringComparison.OrdinalIgnoreCase))
            {
                return group;
            }
        }

        return null;
    }

    private void RecomputeCounts()
    {
        var roots = 0;
        var steps = 0;

        foreach (var group in Groups)
        {
            foreach (var child in group.Children)
            {
                if (child.IsSelected)
                {
                    roots++;
                }

                steps += CountSelectedRecursive(child);
            }
        }

        SelectedRoots = roots;
        SelectedSteps = steps;
    }

    private static int CountSelectedRecursive(TransferIntentNodeViewModel node)
    {
        var total = node.IsSelected ? 1 : 0;

        foreach (var child in node.Children)
        {
            total += CountSelectedRecursive(child);
        }

        return total;
    }

    private static int CountSteps(List<SyncIntent> roots)
    {
        var total = 0;

        foreach (var root in roots)
        {
            total += CountStepsRecursive(root);
        }

        return total;
    }

    private static int CountStepsRecursive(SyncIntent intent)
    {
        var total = 1;

        foreach (var child in intent.NextIntents)
        {
            total += CountStepsRecursive(child);
        }

        return total;
    }

    [RelayCommand(CanExecute = nameof(CanSelectBulk))]
    private void SelectAll()
    {
        SetBulkSelection(true);
        IsMoreMenuOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanSelectBulk))]
    private void DeselectAll()
    {
        SetBulkSelection(false);
        IsMoreMenuOpen = false;
    }

    private bool CanSelectBulk() => !IsRunning && !IsRebuilding && HasPlan;

    private void SetBulkSelection(bool value)
    {
        foreach (var group in Groups)
        {
            foreach (var child in group.Children)
            {
                SetSilentRecursive(child, value);
            }

            group.Recompute();
        }

        RecomputeCounts();
    }

    private static void SetSilentRecursive(TransferIntentNodeViewModel node, bool value)
    {
        node.SetIsSelectedSilent(value);

        foreach (var child in node.Children)
        {
            SetSilentRecursive(child, value);
        }
    }

    [RelayCommand]
    private void ExpandAll()
    {
        SetExpansionAll(true);
        IsMoreMenuOpen = false;
    }

    [RelayCommand]
    private void CollapseAll()
    {
        SetExpansionAll(false);
        IsMoreMenuOpen = false;
    }

    private void SetExpansionAll(bool value)
    {
        foreach (var group in Groups)
        {
            group.IsExpanded = value;

            foreach (var child in group.Children)
            {
                SetExpansionRecursive(child, value);
            }
        }
    }

    private static void SetExpansionRecursive(TransferIntentNodeViewModel node, bool value)
    {
        node.IsExpanded = value;

        foreach (var child in node.Children)
        {
            SetExpansionRecursive(child, value);
        }
    }

    [RelayCommand]
    private void ToggleJournal() => IsJournalOpen = !IsJournalOpen;

    [RelayCommand]
    private void ClearJournal()
    {
        Journal.Clear();
        IsMoreMenuOpen = false;
    }

    partial void OnIsJournalOpenChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(JournalOpenSettingsKey, value ? "true" : "false");
        }
    }

    partial void OnStopOnErrorChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(StopOnErrorSettingsKey, value ? "true" : "false");
        }
    }

    partial void OnUploadIntervalEnabledChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(UploadIntervalEnabledSettingsKey, value ? "true" : "false");
        }
    }

    partial void OnUploadIntervalMinutesTextChanged(string value)
    {
        if (_suppressPersist || ParseUploadInterval(value) is not { } minutes)
        {
            return;
        }

        _settings.SetValue(UploadIntervalMinutesSettingsKey, minutes.ToString(CultureInfo.InvariantCulture));
    }

    private static int? ParseUploadInterval(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
        && minutes is >= 1 and <= MaxUploadIntervalMinutes
            ? minutes
            : null;
}
