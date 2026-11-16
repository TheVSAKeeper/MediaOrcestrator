using KeepShell.Services.Platform;
using MediaOrcestrator.Domain.Comments;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public interface ICommentItemHost
{
    IUiDispatcher Dispatcher { get; }

    Task ExecuteLikeAsync(CommentItemViewModel item, bool liked);

    Task<string?> ExecuteEditAsync(CommentItemViewModel item, string newText);

    Task ExecuteDeleteAsync(CommentItemViewModel item);

    Task ExecuteRestoreAsync(CommentItemViewModel item);

    void StartReply(CommentItemViewModel target);

    void OpenCommentInSource(CommentItemViewModel item);

    void OpenMediaInSource(CommentItemViewModel item);

    void OpenExternalLink(Uri uri);
}

public sealed record CommentItemContext(
    int Depth,
    string SourceTitle,
    string MediaTitle,
    bool HasCommentPermalink,
    bool HasMutations,
    bool HasLikes,
    bool HasAuthors,
    bool HasMediaExternalLink,
    int ReplyCount,
    CommentsLayoutMode Layout,
    string? MediaThumbnailUrl = null,
    bool ShowMediaThumbnail = false);
