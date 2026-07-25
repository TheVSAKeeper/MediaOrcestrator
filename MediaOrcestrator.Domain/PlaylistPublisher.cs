using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Domain;

public sealed class PlaylistPublisher(ActionHolder actionHolder, ILogger<PlaylistPublisher> logger)
{
    public Task<IReadOnlyList<PlaylistInfo>> GetPlaylistsAsync(Source ownerSource, CancellationToken cancellationToken = default)
    {
        return ResolvePlugin(ownerSource).GetPlaylistsAsync(ownerSource.Settings, cancellationToken);
    }

    public Task<PlaylistCreationResult?> CreateAsync(
        Source ownerSource,
        string title,
        PlaylistPrivacy privacy,
        IReadOnlyList<string> videoExternalIds)
    {
        return RunAsync(
            ownerSource,
            $"Плейлист на YouTube: «{title}»",
            videoExternalIds.Count,
            (plugin, progress, ct) => plugin.CreatePlaylistAsync(title, privacy, videoExternalIds, ownerSource.Settings, progress, ct));
    }

    public Task<PlaylistCreationResult?> AppendAsync(
        Source ownerSource,
        string playlistId,
        string playlistTitle,
        IReadOnlyList<string> videoExternalIds)
    {
        return RunAsync(
            ownerSource,
            $"Дозалив в плейлист «{playlistTitle}»",
            videoExternalIds.Count,
            (plugin, progress, ct) => plugin.AddToPlaylistAsync(playlistId, videoExternalIds, ownerSource.Settings, progress, ct));
    }

    private async Task<PlaylistCreationResult?> RunAsync(
        Source ownerSource,
        string actionName,
        int total,
        Func<ISupportsPlaylists, IProgress<int>, CancellationToken, Task<PlaylistCreationResult>> operation)
    {
        var plugin = ResolvePlugin(ownerSource);
        var cts = new CancellationTokenSource();
        var action = actionHolder.Register(actionName, "Подготовка", total, cts);

        try
        {
            var progress = new Progress<int>(done =>
            {
                action.SetProgress(done);
                action.Subtitle = $"Обработано {done} из {total}";
            });

            var result = await operation(plugin, progress, cts.Token);

            action.Subtitle = string.Empty;
            action.Finish(BuildFinalStatus(result));

            logger.LogInformation("Плейлист {PlaylistId}: добавлено {Added}, уже было {Present}, из {Total}, quota={Quota}",
                result.PlaylistId, result.AddedCount, result.AlreadyPresentCount, result.RequestedCount, result.QuotaExhausted);

            return result;
        }
        catch (OperationCanceledException)
        {
            action.MarkCancelled();
            return null;
        }
        catch (Exception exception)
        {
            action.Subtitle = string.Empty;
            action.Fail("Ошибка: " + exception.Message, exception);
            throw;
        }
    }

    private static ISupportsPlaylists ResolvePlugin(Source ownerSource)
    {
        return ownerSource.Type as ISupportsPlaylists
               ?? throw new InvalidOperationException($"Источник «{ownerSource.TitleFull}» не умеет создавать плейлисты");
    }

    private static string BuildFinalStatus(PlaylistCreationResult result)
    {
        var parts = new List<string> { $"добавлено {result.AddedCount}" };

        if (result.AlreadyPresentCount > 0)
        {
            parts.Add($"уже было {result.AlreadyPresentCount}");
        }

        if (result.QuotaExhausted)
        {
            parts.Add("квота исчерпана");
        }

        return "Плейлист: " + string.Join(", ", parts);
    }
}
