using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Youtube;

internal sealed class YoutubeYtDlpReadService(ILogger<YoutubeYtDlpReadService> logger)
{
    public async Task<MediaDto?> GetVideoByIdAsync(
        string videoId,
        YtDlp ytDlp,
        CancellationToken cancellationToken)
    {
        var url = string.Format(YoutubeChannel.VideoUrlTemplate, videoId);
        logger.YtDlpFetchingInfo(videoId);

        var info = await ytDlp.GetVideoInfoAsync(url, cancellationToken);

        if (info is null)
        {
            logger.YtDlpEmptyResponse(videoId);
            return null;
        }

        return BuildMediaDto(videoId, info);
    }

    public MediaDto BuildMediaDto(
        string fallbackVideoId,
        YtDlpVideoInfo info)
    {
        var videoId = string.IsNullOrEmpty(info.Id) ? fallbackVideoId : info.Id;
        logger.YtDlpInfoReceived(info.Title, videoId);

        return MediaDtoFactory.CreateFull(videoId,
            info.Title,
            string.Format(YoutubeChannel.VideoUrlTemplate, videoId),
            info.Thumbnail ?? "",
            info.Description,
            info.Duration,
            info.Uploader,
            info.UploadDate?.ToString("O"),
            info.ViewCount);
    }
}
