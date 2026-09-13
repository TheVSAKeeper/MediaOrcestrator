using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed partial class ReplyEditorViewModel : ObservableObject, ICommentsRowViewModel
{
    private readonly Func<ReplyEditorViewModel, Task> _submitHandler;
    private readonly Action<ReplyEditorViewModel> _cancelHandler;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private CommentAuthorOptionViewModel? _selectedAuthor;

    [ObservableProperty]
    private bool _isAuthorsLoading;

    [ObservableProperty]
    private bool _isSubmitting;

    [ObservableProperty]
    private string? _errorMessage;

    public ReplyEditorViewModel(
        string sourceId,
        string externalMediaId,
        string? parentExternalCommentId,
        int depth,
        bool hasAuthors,
        string? initialText,
        Func<ReplyEditorViewModel, Task> submitHandler,
        Action<ReplyEditorViewModel> cancelHandler)
    {
        _submitHandler = submitHandler;
        _cancelHandler = cancelHandler;

        SourceId = sourceId;
        ExternalMediaId = externalMediaId;
        ParentExternalCommentId = parentExternalCommentId;
        Depth = depth;
        HasAuthors = hasAuthors;
        IndentMargin = new(24 * Math.Min(depth, 6), 0, 0, 0);
        Text = initialText ?? string.Empty;

        Authors = [];
    }

    public string SourceId { get; }

    public string ExternalMediaId { get; }

    public string? ParentExternalCommentId { get; }

    public int Depth { get; }

    public bool HasAuthors { get; }

    public System.Windows.Thickness IndentMargin { get; }

    public ObservableCollection<CommentAuthorOptionViewModel> Authors { get; }

    public bool CanSubmit => !IsSubmitting && !string.IsNullOrWhiteSpace(Text);

    [RelayCommand(CanExecute = nameof(CanSubmitInternal))]
    private async Task SubmitAsync()
    {
        if (IsSubmitting)
        {
            return;
        }

        ErrorMessage = null;
        IsSubmitting = true;

        try
        {
            await _submitHandler(this);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancelHandler(this);
    }

    public void ReplaceAuthors(IEnumerable<CommentAuthorOptionViewModel> authors)
    {
        Authors.Clear();

        foreach (var author in authors)
        {
            Authors.Add(author);
        }

        if (SelectedAuthor is null || !Authors.Any(a => a.Id == SelectedAuthor.Id))
        {
            SelectedAuthor = Authors.FirstOrDefault(a => a.IsDefault) ?? Authors.FirstOrDefault();
        }
    }

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanSubmit));
        SubmitCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSubmittingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSubmit));
        SubmitCommand.NotifyCanExecuteChanged();
    }

    private bool CanSubmitInternal() => CanSubmit;
}
