
using MediaOrcestrator.Core.Services;

namespace MediaOrcestrator.Domain
{
    public class Orcestrator(PluginManager pluginManager)
    {
        public List<IMediaSource> GetSources()
        {
            return pluginManager.MediaSources;
        }

        public void Init()
        {
            pluginManager.Init();
        }
    }

    public static class OrcestratorBuilder
    {
        public static Orcestrator Construct()
        {
            return new Orcestrator(new PluginManager());
        }
    }
}
