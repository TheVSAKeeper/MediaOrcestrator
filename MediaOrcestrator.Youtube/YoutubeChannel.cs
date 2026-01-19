using MediaOrcestrator.Core.Services;

namespace MediaOrcestrator.Youtube
{
    public class YoutubeChannel : IMediaSource
    {
        public ChannelType ChannelType => ChannelType.OnlyUpload;

        public IMedia[] GetMedia()
        {
            throw new NotImplementedException();
        }

        public IMedia GetMediaById()
        {
            throw new NotImplementedException();
        }

        public IMedia Download()
        {
            throw new NotImplementedException();
        }

        public void Upload(IMedia media)
        {
            Console.WriteLine("я загрузил брат " + media.Title);
        }
    }

    public class RutubeMedia : IMedia
    {
        public string Title { get; set; }
        public string Description { get; set; }
    }
}
