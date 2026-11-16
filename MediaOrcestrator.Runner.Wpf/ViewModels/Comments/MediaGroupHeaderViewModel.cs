using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed partial class MediaGroupHeaderViewModel : ObservableObject, ICommentsRowViewModel
{
    private readonly Func<MediaGroupHeaderViewModel, Task> _refreshAllHandler;
    private readonly IUiDispatcher _dispatcher;
    private ImageSource? _thumbnailSource;
    private bool _thumbnailLoadStarted;

    [ObservableProperty]
    private bool _isRefreshing;

    public MediaGroupHeaderViewModel(
        string groupKey,
        string mediaTitle,
        int commentsCount,
        IReadOnlyList<MediaGroupSourceInfo> sources,
        Func<MediaGroupHeaderViewModel, Task> refreshAllHandler,
        Func<MediaGroupSourceActionViewModel, Task> refreshOneHandler,
        Func<MediaGroupSourceActionViewModel, Task> openOneHandler,
        IUiDispatcher dispatcher,
        string? thumbnailUrl = null)
    {
        _refreshAllHandler = refreshAllHandler;
        _dispatcher = dispatcher;
        GroupKey = groupKey;
        MediaTitle = mediaTitle;
        CommentsCount = commentsCount;
        Sources = sources;
        ThumbnailUrl = thumbnailUrl;
        CommentsCountText = commentsCount switch
        {
            1 => "1 комментарий",
            >= 2 and <= 4 => $"{commentsCount} комментария",
            _ => $"{commentsCount} комментариев",
        };

        SourceActions = sources
            .Select(s => new MediaGroupSourceActionViewModel(s, refreshOneHandler, openOneHandler))
            .ToList();
    }

    public string GroupKey { get; }

    public string MediaTitle { get; }

    public int CommentsCount { get; }

    public string CommentsCountText { get; }

    public IReadOnlyList<MediaGroupSourceInfo> Sources { get; }

    public IReadOnlyList<MediaGroupSourceActionViewModel> SourceActions { get; }

    public bool HasMultipleSources => Sources.Count > 1;

    public bool HasSingleSource => Sources.Count <= 1;

    public string? ThumbnailUrl { get; }

    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailUrl);

    public ImageSource? ThumbnailSource
    {
        get
        {
            if (_thumbnailSource is not null)
            {
                return _thumbnailSource;
            }

            if (!_thumbnailLoadStarted && !string.IsNullOrEmpty(ThumbnailUrl))
            {
                _thumbnailLoadStarted = true;
                _ = LoadThumbnailAsync();
            }

            return null;
        }
    }

    public bool CanRefresh => Sources.Any(s => s.HasFetchSupport);

    [RelayCommand(CanExecute = nameof(CanExecuteRefresh))]
    private async Task RefreshAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;

        try
        {
            await _refreshAllHandler(this);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task LoadThumbnailAsync()
    {
        var url = ThumbnailUrl;

        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        var image = await MediaThumbnailCache.GetAsync(url);

        if (image is null)
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            _thumbnailSource = image;
            OnPropertyChanged(nameof(ThumbnailSource));
        });
    }

    private bool CanExecuteRefresh() => CanRefresh && !IsRefreshing;
}

public sealed record MediaGroupSourceInfo(
    string SourceId,
    string ExternalMediaId,
    string SourceTitle,
    bool HasMediaExternalLink,
    bool HasFetchSupport);

public sealed partial class MediaGroupSourceActionViewModel : ObservableObject
{
    private readonly Func<MediaGroupSourceActionViewModel, Task> _refreshHandler;
    private readonly Func<MediaGroupSourceActionViewModel, Task> _openHandler;

    [ObservableProperty]
    private bool _isRefreshing;

    public MediaGroupSourceActionViewModel(
        MediaGroupSourceInfo info,
        Func<MediaGroupSourceActionViewModel, Task> refreshHandler,
        Func<MediaGroupSourceActionViewModel, Task> openHandler)
    {
        _refreshHandler = refreshHandler;
        _openHandler = openHandler;
        SourceId = info.SourceId;
        ExternalMediaId = info.ExternalMediaId;
        SourceTitle = info.SourceTitle;
        HasMediaExternalLink = info.HasMediaExternalLink;
        CanFetch = info.HasFetchSupport;
        OpenLabel = $"Открыть {info.SourceTitle}";
        RefreshLabel = $"Обновить {info.SourceTitle}";
    }

    public string SourceId { get; }

    public string ExternalMediaId { get; }

    public string SourceTitle { get; }

    public bool HasMediaExternalLink { get; }

    public bool CanFetch { get; }

    public string OpenLabel { get; }

    public string RefreshLabel { get; }

    [RelayCommand(CanExecute = nameof(HasMediaExternalLink))]
    private async Task OpenAsync()
    {
        await _openHandler(this);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteRefresh))]
    private async Task RefreshAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;

        try
        {
            await _refreshHandler(this);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private bool CanExecuteRefresh() => CanFetch && !IsRefreshing;
}
