using CommunityToolkit.Mvvm.ComponentModel;
using KeepShell.Services.Platform;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed record CommentReplyStatusOption(CommentsReplyStatusFilter Key, string Label)
{
    public override string ToString() => Label;
}

public sealed record CommentLayoutOption(CommentsLayoutMode Mode, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class CommentAuthorOptionViewModel : ObservableObject
{
    private readonly IUiDispatcher _dispatcher;
    private ImageSource? _avatarSource;
    private bool _avatarLoadStarted;

    public CommentAuthorOptionViewModel(string id, string name, string? avatarUrl, bool isDefault, IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Id = id;
        Name = string.IsNullOrEmpty(name) ? "(без имени)" : name;
        AvatarUrl = avatarUrl;
        IsDefault = isDefault;
        Initial = !string.IsNullOrEmpty(Name) ? char.ToUpperInvariant(Name[0]).ToString() : "•";
    }

    public string Id { get; }

    public string Name { get; }

    public string? AvatarUrl { get; }

    public bool IsDefault { get; }

    public string Initial { get; }

    public ImageSource? AvatarSource
    {
        get
        {
            if (_avatarSource is not null)
            {
                return _avatarSource;
            }

            if (!_avatarLoadStarted && !string.IsNullOrEmpty(AvatarUrl))
            {
                _avatarLoadStarted = true;
                _ = LoadAvatarAsync();
            }

            return null;
        }
    }

    public override string ToString() => Name;

    private async Task LoadAvatarAsync()
    {
        var url = AvatarUrl;

        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        var image = await AvatarCache.GetAsync(url);

        if (image is null)
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            _avatarSource = image;
            OnPropertyChanged(nameof(AvatarSource));
        });
    }
}
