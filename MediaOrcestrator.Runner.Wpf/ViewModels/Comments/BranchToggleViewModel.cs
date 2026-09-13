using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed partial class BranchToggleViewModel : ObservableObject, ICommentsRowViewModel
{
    private readonly Func<BranchToggleViewModel, Task> _toggleHandler;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isToggling;

    public BranchToggleViewModel(
        string rootId,
        int totalCount,
        int hiddenCount,
        int depth,
        bool isExpanded,
        Func<BranchToggleViewModel, Task> toggleHandler)
    {
        _toggleHandler = toggleHandler;
        RootId = rootId;
        TotalCount = totalCount;
        HiddenCount = hiddenCount;
        Depth = depth;
        _isExpanded = isExpanded;
        IndentMargin = new(24 * Math.Min(depth, 6), 0, 0, 0);
    }

    public string RootId { get; }

    public int TotalCount { get; }

    public int HiddenCount { get; }

    public int Depth { get; }

    public System.Windows.Thickness IndentMargin { get; }

    public string Label => IsExpanded ? FormatCollapseLabel(TotalCount) : FormatExpandLabel(HiddenCount);

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (IsToggling)
        {
            return;
        }

        IsToggling = true;

        try
        {
            await _toggleHandler(this);
        }
        finally
        {
            IsToggling = false;
        }
    }

    private static string FormatExpandLabel(int hidden) =>
        $"Ещё {hidden} {PluralAnswers(hidden)} – развернуть";

    private static string FormatCollapseLabel(int total) =>
        $"Свернуть {total} {PluralAnswers(total)}";

    private static string PluralAnswers(int n)
    {
        if (n % 100 is >= 11 and <= 14)
        {
            return "ответов";
        }

        return (n % 10) switch
        {
            1 => "ответ",
            >= 2 and <= 4 => "ответа",
            _ => "ответов",
        };
    }
}
