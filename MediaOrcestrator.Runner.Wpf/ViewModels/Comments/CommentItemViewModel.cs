using CommunityToolkit.Mvvm.ComponentModel;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain.Comments;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed partial class CommentItemViewModel : ObservableObject, ICommentsRowViewModel
{
    private const int DeleteConfirmTimeoutMs = 5000;

    private readonly ICommentItemHost _host;
    private readonly DateTime _publishedAtUtc;
    private IUiTimer? _deleteConfirmTimer;
    private string _displayText = string.Empty;
    private string? _currentSearch;
    private IReadOnlyList<Inline>? _cachedInlines;
    private ImageSource? _authorAvatarSource;
    private bool _avatarLoadStarted;
    private ImageSource? _mediaThumbnailSource;
    private bool _mediaThumbnailLoadStarted;

    [ObservableProperty]
    private bool _isLikeBusy;

    [ObservableProperty]
    private bool _isMutationBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewing))]
    private bool _isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewing))]
    private bool _isConfirmingDelete;

    [ObservableProperty]
    private string _editText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isDeleted;

    [ObservableProperty]
    private int _likeCount;

    [ObservableProperty]
    private bool _likedByMe;

    public CommentItemViewModel(CommentRecord record, CommentItemContext context, ICommentItemHost host, string? search = null)
    {
        _host = host;
        Record = record;
        Context = context;
        _currentSearch = string.IsNullOrEmpty(search) ? null : search;

        Id = record.Id;
        AuthorName = string.IsNullOrEmpty(record.AuthorName) ? "(без имени)" : record.AuthorName;
        AuthorInitial = !string.IsNullOrEmpty(AuthorName)
            ? char.ToUpperInvariant(AuthorName[0]).ToString()
            : "•";
        AuthorAvatarUrl = record.AuthorAvatarUrl;
        IsAuthor = record.IsAuthor;
        LikedByAuthor = record.LikedByAuthor;
        Depth = context.Depth;
        IndentMargin = new(24 * Math.Min(Depth, 6), 0, 0, 0);
        _publishedAtUtc = record.PublishedAt.Kind == DateTimeKind.Utc
            ? record.PublishedAt
            : record.PublishedAt.ToUniversalTime();
        AbsoluteDateText = record.PublishedAt.ToLocalTime().ToString("g");
        SourceTitle = context.SourceTitle;
        MediaTitle = context.MediaTitle;
        HasCommentPermalink = context.HasCommentPermalink;
        HasMutations = context.HasMutations;
        HasLikes = context.HasLikes;
        HasAuthors = context.HasAuthors;
        HasMediaExternalLink = context.HasMediaExternalLink;
        CanEdit = record.CanEdit;
        CanDelete = record.CanDelete;
        ReplyCount = context.ReplyCount;
        MediaThumbnailUrl = context.MediaThumbnailUrl;
        ShowMediaThumbnail = context.ShowMediaThumbnail && !string.IsNullOrEmpty(context.MediaThumbnailUrl);

        _displayText = record.Text ?? string.Empty;
        IsDeleted = record.IsDeleted;
        LikeCount = record.LikeCount.GetValueOrDefault();
        LikedByMe = record.LikedByMe;
    }

    public CommentRecord Record { get; }

    public CommentItemContext Context { get; }

    public string Id { get; }

    public string AuthorName { get; }

    public string AuthorInitial { get; }

    public string? AuthorAvatarUrl { get; }

    public ImageSource? AuthorAvatarSource
    {
        get
        {
            if (_authorAvatarSource is not null)
            {
                return _authorAvatarSource;
            }

            if (!_avatarLoadStarted && !string.IsNullOrEmpty(AuthorAvatarUrl))
            {
                _avatarLoadStarted = true;
                _ = LoadAvatarAsync();
            }

            return null;
        }
    }

    public string? MediaThumbnailUrl { get; }

    public bool ShowMediaThumbnail { get; }

    public ImageSource? MediaThumbnailSource
    {
        get
        {
            if (_mediaThumbnailSource is not null)
            {
                return _mediaThumbnailSource;
            }

            if (!_mediaThumbnailLoadStarted && ShowMediaThumbnail && !string.IsNullOrEmpty(MediaThumbnailUrl))
            {
                _mediaThumbnailLoadStarted = true;
                _ = LoadMediaThumbnailAsync();
            }

            return null;
        }
    }

    public bool IsAuthor { get; }

    public bool LikedByAuthor { get; }

    public int Depth { get; }

    public Thickness IndentMargin { get; }

    public string AbsoluteDateText { get; }

    public string DateText => Context.Layout == CommentsLayoutMode.Flat
        ? RelativeTimeFormatter.Format(_publishedAtUtc, DateTime.UtcNow, AbsoluteDateText)
        : AbsoluteDateText;

    public string SourceTitle { get; }

    public string MediaTitle { get; }

    public bool HasCommentPermalink { get; }

    public bool HasMutations { get; }

    public bool HasLikes { get; }

    public bool HasAuthors { get; }

    public bool HasMediaExternalLink { get; }

    public bool CanEdit { get; }

    public bool CanDelete { get; }

    public int ReplyCount { get; }

    public bool IsRoot => Depth == 0;

    public bool IsViewing => !IsEditing && !IsConfirmingDelete;

    public string DisplayText
    {
        get => _displayText;
        private set
        {
            if (_displayText == value)
            {
                return;
            }

            _displayText = value;
            _cachedInlines = null;
            OnPropertyChanged();
            RaiseInlinesChanged();
        }
    }

    public IEnumerable<Inline> Inlines => _cachedInlines ??= CommentTextRenderer.BuildInlines(DisplayText, _currentSearch, _host).ToList();

    public void RefreshRelativeTime()
    {
        if (Context.Layout == CommentsLayoutMode.Flat)
        {
            OnPropertyChanged(nameof(DateText));
        }
    }

    public void ApplySearch(string? search)
    {
        if (string.Equals(_currentSearch, search, StringComparison.Ordinal))
        {
            return;
        }

        _currentSearch = search;
        _cachedInlines = null;
        RaiseInlinesChanged();
    }

    private async Task LoadAvatarAsync()
    {
        var url = AuthorAvatarUrl;

        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        var image = await AvatarCache.GetAsync(url);

        if (image is null)
        {
            return;
        }

        await _host.Dispatcher.InvokeAsync(() =>
        {
            _authorAvatarSource = image;
            OnPropertyChanged(nameof(AuthorAvatarSource));
        });
    }

    private async Task LoadMediaThumbnailAsync()
    {
        var url = MediaThumbnailUrl;

        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        var image = await MediaThumbnailCache.GetAsync(url);

        if (image is null)
        {
            return;
        }

        await _host.Dispatcher.InvokeAsync(() =>
        {
            _mediaThumbnailSource = image;
            OnPropertyChanged(nameof(MediaThumbnailSource));
        });
    }

    private void RaiseInlinesChanged()
    {
        OnPropertyChanged(nameof(Inlines));
    }
}
