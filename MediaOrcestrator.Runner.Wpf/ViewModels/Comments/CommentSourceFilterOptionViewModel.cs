using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed partial class CommentSourceFilterOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked;

    public CommentSourceFilterOptionViewModel(string sourceId, string title)
    {
        SourceId = sourceId;
        Title = title;
    }

    public string SourceId { get; }

    public string Title { get; }
}
