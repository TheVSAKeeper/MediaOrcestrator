using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class FilterOptionViewModel : ObservableObject
{
    private readonly Action<FilterOptionViewModel>? _onChecked;

    [ObservableProperty]
    private bool _isChecked;

    public FilterOptionViewModel(string id, string title, Action<FilterOptionViewModel>? onChecked = null, string? groupName = null)
    {
        Id = id;
        Title = title;
        _onChecked = onChecked;
        GroupName = groupName;
    }

    public string Id { get; }

    public string Title { get; }

    public string? GroupName { get; }

    partial void OnIsCheckedChanged(bool value)
    {
        _onChecked?.Invoke(this);
    }
}
