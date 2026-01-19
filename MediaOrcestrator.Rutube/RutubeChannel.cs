using MediaOrcestrator.Core.Services; // представляем что через нугет подключили

namespace MediaOrcestrator.Rutube
{
    public class RutubeChannel : IMediaSource
    {
        public ChannelType ChannelType => ChannelType.OnlyUpload;

        public string Name => "Rutube";

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
            Console.WriteLine("я загрузил брат");
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
