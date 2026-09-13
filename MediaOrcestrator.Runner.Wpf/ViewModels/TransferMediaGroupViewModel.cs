using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class TransferMediaGroupViewModel : ObservableObject
{
    private readonly TransfersViewModel _owner;
    private bool _suppressBroadcast;

    [ObservableProperty]
    private bool? _isAllSelected = false;

    [ObservableProperty]
    private bool _isExpanded = true;

    public TransferMediaGroupViewModel(TransfersViewModel owner, Media media)
    {
        _owner = owner;
        Media = media;
        Children = [];
    }

    public Media Media { get; }

    public string Title => Media.Title;

    public RangeObservableCollection<TransferIntentNodeViewModel> Children { get; }

    internal void Recompute()
    {
        if (Children.Count == 0)
        {
            _suppressBroadcast = true;
            IsAllSelected = false;
            _suppressBroadcast = false;
            return;
        }

        var selectedCount = 0;
        var totalDescendants = 0;
        var selectedDescendants = 0;

        foreach (var child in Children)
        {
            if (child.IsSelected)
            {
                selectedCount++;
            }

            CountRecursive(child, ref selectedDescendants, ref totalDescendants);
        }

        bool? newState;

        if (selectedCount == 0 && selectedDescendants == 0)
        {
            newState = false;
        }
        else if (selectedCount == Children.Count && selectedDescendants == totalDescendants)
        {
            newState = true;
        }
        else
        {
            newState = null;
        }

        _suppressBroadcast = true;
        IsAllSelected = newState;
        _suppressBroadcast = false;
    }

    partial void OnIsAllSelectedChanged(bool? value)
    {
        if (_suppressBroadcast)
        {
            return;
        }

        if (value is null)
        {
            _suppressBroadcast = true;
            IsAllSelected = true;
            _suppressBroadcast = false;

            foreach (var child in Children)
            {
                SetRecursiveSilent(child, true);
            }

            _owner.OnGroupBulkSelection();
            return;
        }

        foreach (var child in Children)
        {
            SetRecursiveSilent(child, value.Value);
        }

        _owner.OnGroupBulkSelection();
    }

    private static void SetRecursiveSilent(TransferIntentNodeViewModel node, bool value)
    {
        node.SetIsSelectedSilent(value);

        foreach (var child in node.Children)
        {
            SetRecursiveSilent(child, value);
        }
    }

    [RelayCommand]
    private void SelectAllInGroup()
    {
        foreach (var child in Children)
        {
            SetRecursiveSilent(child, true);
        }

        Recompute();
        _owner.OnGroupBulkSelection();
    }

    [RelayCommand]
    private void DeselectAllInGroup()
    {
        foreach (var child in Children)
        {
            SetRecursiveSilent(child, false);
        }

        Recompute();
        _owner.OnGroupBulkSelection();
    }

    [RelayCommand]
    private void OpenInMedia()
    {
        _owner.OpenInMedia(Media);
    }

    private static void CountRecursive(TransferIntentNodeViewModel node, ref int selected, ref int total)
    {
        foreach (var child in node.Children)
        {
            total++;

            if (child.IsSelected)
            {
                selected++;
            }

            CountRecursive(child, ref selected, ref total);
        }
    }
}
