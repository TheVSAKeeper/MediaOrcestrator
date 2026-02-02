using MediaOrcestrator.Core;
using MediaOrcestrator.Modules;
using YoutubeExplode;
using YoutubeExplode.Channels;
using YoutubeExplode.Videos.Streams;

namespace MediaOrcestrator.Youtube;

public class YoutubeChannel : ISourceType
{
    public SyncDirection ChannelType => SyncDirection.OnlyUpload;

    public string Name => "Youtube";

    public IEnumerable<SourceSettings> SettingsKeys { get; } =
    [
        new()
        {
            Key = "channel_id",
            IsRequired = true,
            Title = "идентификатор канала",
        },
    ];

    public async IAsyncEnumerable<MediaDto> GetMedia(Dictionary<string, string> settings)
    {
        //var channelUrl = "https://www.youtube.com/@bobito217";

        var channelUrl = settings["channel_id"];
        var youtubeClient = new YoutubeClient();
        var channel = await GetChannel(youtubeClient, channelUrl);
        var uploads = youtubeClient.Channels.GetUploadsAsync(channel.Id);
        await foreach (var video in uploads)
        {
            yield return new MediaDto
            {
                Id = video.Id.Value,
                Title = video.Title,
                DataPath = video.Url,
                PreviewPath = video.Thumbnails.FirstOrDefault()!.Url,
            };
        }
    }

    private readonly Func<YoutubeClient, string, Task<Channel?>>[] _parsers =
    [
        async (youtubeClient, url ) => ChannelId.TryParse(url) is { } id ? await youtubeClient.Channels.GetAsync(id) : null,
        async (youtubeClient, url ) => ChannelSlug.TryParse(url) is { } slug ? await youtubeClient.Channels.GetBySlugAsync(slug) : null,
        async (youtubeClient, url ) => ChannelHandle.TryParse(url) is { } handle ? await youtubeClient.Channels.GetByHandleAsync(handle) : null,
        async (youtubeClient, url ) => UserName.TryParse(url) is { } userName ? await youtubeClient.Channels.GetByUserAsync(userName) : null,
    ];

    public async Task<Channel?> GetChannel(YoutubeClient client, string channelUrl)
    {
        foreach (var parser in _parsers)
        {
            var channel = await parser(client, channelUrl);

            if (channel != null)
            {
                return channel;
            }
        }

        return null;
    }

    public MediaDto GetMediaById()
    {
        throw new NotImplementedException();
    }

    public async Task<MediaDto> Download(Dictionary<string, string> settings)
    {
        // todo дублирование
        var channelUrl = settings["channel_id"];
        var youtubeClient = new YoutubeClient();

        var video = await youtubeClient.Videos.GetAsync(channelUrl);
        var streamManifest = await youtubeClient.Videos.Streams.GetManifestAsync(channelUrl);

        var highestAudioStream = (IAudioStreamInfo)streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();
        var highestVideoStream = (IVideoStreamInfo)streamManifest.GetVideoOnlyStreams().GetWithHighestBitrate();

        var stream = DownloadItemStream.Create(0,
            Path.Combine(path, ".temp"),
            path,
            video,
            highestAudioStream,
            highestVideoStream);

        var item = DownloadItem.Create(url, [stream], video).GetValueOrDefault();

        Console.WriteLine("ютубный я загрузил брат ");
        throw new NotImplementedException();
    }

    private async Task DownloadCombinedStream(DownloadItemStream downloadStream, DownloadItem downloadItem, CancellationToken cancellationToken = default)
    {
        var audioPath = downloadStream.TempPath.AddSuffixToFileName("audio");
        var videoPath = downloadStream.TempPath.AddSuffixToFileName("video");

        if (downloadStream.AudioStreamInfo == null || downloadStream.VideoStreamInfo == null)
        {
        //    logger.LogError("Не удалось объединить ({MethodName}). Нет видео или аудио", nameof(DownloadCombinedStream));
            return;
        }

        var audioTask = DownloadWithProgressAsync(downloadStream.AudioStreamInfo,
            audioPath,
            downloadStream.Title,
            downloadItem.Video.Title,
            cancellationToken);

        var videoTask = DownloadWithProgressAsync(downloadStream.VideoStreamInfo,
            videoPath,
            downloadStream.Title,
            downloadItem.Video.Title,
            cancellationToken);

        await Task.WhenAll(audioTask.AsTask(), videoTask.AsTask());

        //logger.LogDebug("Попытка объединить видео и аудио: {Id} {StreamId}", downloadItem.Id, downloadStream.Id);

        double oldPercent = -1;

        Progress<double> progress = new(percent =>
        {
            if (percent - oldPercent < 0.1)
            {
                return;
            }

        //    logger.LogDebug("Объединение: {Percent:P2}", percent);
            oldPercent = percent;
        });

        // todo :)
        var converter = new FFmpegConverter(new FFmpeg(null));
        await converter.MergeMediaAsync(downloadStream.FilePath, [audioPath, videoPath], progress, cancellationToken);

      //  logger.LogDebug("Успешно объединено видео и аудио: {Id} {StreamId}", downloadItem.Id, downloadStream.Id);
    }

    public ValueTask DownloadWithProgressAsync(IStreamInfo streamInfo, string path, string streamTitle, string videoTitle, CancellationToken cancellationToken)
    {
        double oldPercent = -1;

        var streamType = streamInfo switch
        {
            AudioOnlyStreamInfo => "Audio",
            VideoOnlyStreamInfo => "Video",
            MuxedStreamInfo => "Muxed",
            _ => "Unknown",
        };

        Progress<double> progress = new(percent =>
        {
            if (percent - oldPercent < 0.1)
            {
                return;
            }

           // logger.LogDebug("{StreamType}: {Percent:P2} {VideoTitle} {StreamTitle}", streamType, percent, videoTitle, streamTitle);
            oldPercent = percent;
        });

        return DownloadAsync(streamInfo, path, progress, cancellationToken);
    }

    public ValueTask DownloadAsync(IStreamInfo stream, string path, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        return youtubeClient.Videos.Streams.DownloadAsync(stream, path, progress, cancellationToken);
    }

    public void Upload(MediaDto media, Dictionary<string, string> settings)
    {
        Console.WriteLine("ютубный я загрузил брат " + media.Title);
    }
}
