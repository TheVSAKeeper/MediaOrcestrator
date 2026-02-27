using MediaOrcestrator.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace MediaOrcestrator.Domain;

public class PluginManager(IServiceProvider serviceProvider)
{
    public Dictionary<string, ISourceType> MediaSources { get; set; }

    public void Init(string pluginPath)
    {
        //  var path1 = "..\\..\\..\\..\\ModuleBuilds";
        //  var path1 = "ModuleBuilds";
        var implementations = InterfaceScanner.FindImplementations(pluginPath, typeof(ISourceType));
        MediaSources = new();
        foreach (var x in implementations)
        {
            var id = x.Assembly.FullName?.Split(",")[0];
            var instance = (ISourceType)ActivatorUtilities.CreateInstance(serviceProvider, x.Type);

            if (instance.SettingsKeys != null && instance.SettingsKeys.Any(x => x.Key.StartsWith("_system", StringComparison.Ordinal)))
            {
                // todo логи
                continue;
            }

            MediaSources.Add(id, instance);
        }
    }
}
