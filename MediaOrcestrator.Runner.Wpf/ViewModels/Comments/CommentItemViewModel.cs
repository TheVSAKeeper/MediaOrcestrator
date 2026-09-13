using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain.Comments;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed partial class CommentItemViewModel : ObservableObject, ICommentsRowViewModel
{
    private const int DeleteConfirmTimeoutMs = 5000;

    private readonly ICommentItemHost _host;
    private readonly DateTime _publishedAtUtc;
    private DispatcherTimer? _deleteConfirmTimer;
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

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _authorAvatarSource = image;
            OnPropertyChanged(nameof(AuthorAvatarSource));
        });
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

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _mediaThumbnailSource = image;
            OnPropertyChanged(nameof(MediaThumbnailSource));
        });
    }

    public bool IsAuthor { get; }

    public bool LikedByAuthor { get; }

    public int Depth { get; }

    public Thickness IndentMargin { get; }

    public string AbsoluteDateText { get; }

    public string DateText => Context.Layout == CommentsLayoutMode.Flat
        ? RelativeTimeFormatter.Format(_publishedAtUtc, DateTime.UtcNow, AbsoluteDateText)
        : AbsoluteDateText;

    public void RefreshRelativeTime()
    {
        if (Context.Layout == CommentsLayoutMode.Flat)
        {
            OnPropertyChanged(nameof(DateText));
        }
    }

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

    [RelayCommand]
    private async Task ToggleLikeAsync()
    {
        if (!HasLikes || IsLikeBusy)
        {
            return;
        }

        var nextLiked = !LikedByMe;
        IsLikeBusy = true;
        ErrorMessage = null;

        try
        {
            await _host.ExecuteLikeAsync(this, nextLiked);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLikeBusy = false;
        }
    }

    [RelayCommand]
    private void StartReply()
    {
        _host.StartReply(this);
    }

    [RelayCommand]
    private void StartEdit()
    {
        if (!HasMutations || !CanEdit || IsDeleted)
        {
            return;
        }

        ErrorMessage = null;
        EditText = DisplayText;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        EditText = string.Empty;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (!IsEditing || IsMutationBusy)
        {
            return;
        }

        var newText = (EditText ?? string.Empty).Trim();

        if (newText.Length == 0)
        {
            ErrorMessage = "Текст не должен быть пустым.";
            return;
        }

        if (string.Equals(newText, DisplayText, StringComparison.Ordinal))
        {
            IsEditing = false;
            return;
        }

        IsMutationBusy = true;
        ErrorMessage = null;

        try
        {
            var updated = await _host.ExecuteEditAsync(this, newText);

            if (updated is not null)
            {
                DisplayText = updated;
            }

            IsEditing = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsMutationBusy = false;
        }
    }

    [RelayCommand]
    private void RequestDelete()
    {
        if (!HasMutations || !CanDelete || IsDeleted)
        {
            return;
        }

        ErrorMessage = null;
        IsConfirmingDelete = true;
        StartDeleteAutoCancel();
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (!IsConfirmingDelete || IsMutationBusy)
        {
            return;
        }

        StopDeleteAutoCancel();
        IsMutationBusy = true;

        try
        {
            await _host.ExecuteDeleteAsync(this);
            IsDeleted = true;
            IsConfirmingDelete = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsMutationBusy = false;
        }
    }

    [RelayCommand]
    private void CancelDelete()
    {
        StopDeleteAutoCancel();
        IsConfirmingDelete = false;
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (!HasMutations || !IsDeleted || IsMutationBusy)
        {
            return;
        }

        IsMutationBusy = true;
        ErrorMessage = null;

        try
        {
            await _host.ExecuteRestoreAsync(this);
            IsDeleted = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsMutationBusy = false;
        }
    }

    [RelayCommand]
    private void OpenInSource()
    {
        if (HasCommentPermalink)
        {
            _host.OpenCommentInSource(this);
        }
    }

    [RelayCommand]
    private void OpenMediaInSource()
    {
        if (HasMediaExternalLink)
        {
            _host.OpenMediaInSource(this);
        }
    }

    public void ApplyLikeUpdate(bool likedByMe, int likeCount)
    {
        LikedByMe = likedByMe;
        LikeCount = likeCount;
    }

    public void ApplyDeletedUpdate(bool isDeleted)
    {
        IsDeleted = isDeleted;
    }

    private void StartDeleteAutoCancel()
    {
        StopDeleteAutoCancel();

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        _deleteConfirmTimer = new(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(DeleteConfirmTimeoutMs),
        };

        _deleteConfirmTimer.Tick += (_, _) =>
        {
            StopDeleteAutoCancel();
            IsConfirmingDelete = false;
        };

        _deleteConfirmTimer.Start();
    }

    private void StopDeleteAutoCancel()
    {
        if (_deleteConfirmTimer is null)
        {
            return;
        }

        _deleteConfirmTimer.Stop();
        _deleteConfirmTimer = null;
    }

    private void RaiseInlinesChanged()
    {
        OnPropertyChanged(nameof(Inlines));
    }
}

public interface ICommentItemHost
{
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
