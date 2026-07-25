namespace MediaOrcestrator.Modules;

public enum PlaylistPrivacy
{
    None = 0,
    Private = 1,
    Unlisted = 2,
    Public = 3,
}

public sealed record PlaylistInfo(string Id, string Title, int ItemCount);

public sealed record PlaylistCreationResult(
    string PlaylistId,
    string? Url,
    int AddedCount,
    int RequestedCount,
    bool QuotaExhausted)
{
    public int AlreadyPresentCount { get; init; }
}

public interface ISupportsPlaylists
{
    Task<IReadOnlyList<PlaylistInfo>> GetPlaylistsAsync(
        Dictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    Task<PlaylistCreationResult> CreatePlaylistAsync(
        string title,
        PlaylistPrivacy privacy,
        IReadOnlyList<string> videoExternalIds,
        Dictionary<string, string> settings,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    Task<PlaylistCreationResult> AddToPlaylistAsync(
        string playlistId,
        IReadOnlyList<string> videoExternalIds,
        Dictionary<string, string> settings,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
