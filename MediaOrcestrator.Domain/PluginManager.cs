using MediaOrcestrator.Core.Services;

namespace MediaOrcestrator.Domain
{
    public class PluginManager
    {
        public List<IMediaSource> MediaSources { get; set; }

        public void Init()
        {
            var path1 = "..\\..\\..\\..\\ModuleBuilds";
            var scanner = new InterfaceScanner();
            var myInterfaceType = typeof(IMediaSource); // Пример интерфейса
            var implementations = scanner.FindImplementations(path1, myInterfaceType);
            MediaSources = new List<IMediaSource>();
            foreach (var x in implementations)
            {
                var id = x.Assembly.FullName;
                var aaaa = (IMediaSource)Activator.CreateInstance(x.Type);
                var xyy = aaaa.Name;
                MediaSources.Add(aaaa);
            }
        }
    }
}
