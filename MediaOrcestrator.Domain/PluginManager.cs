using MediaOrcestrator.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Domain;

public class PluginManager(IServiceProvider serviceProvider, ILogger<PluginManager> logger)
{
    public Dictionary<string, ISourceType> MediaSources { get; private set; } = new();

    public void Init(string pluginPath)
    {
        //  var path1 = "..\\..\\..\\..\\ModuleBuilds";
        //  var path1 = "ModuleBuilds";
        var implementations = InterfaceScanner.FindImplementations(pluginPath, typeof(ISourceType));
        MediaSources = new();

        foreach (var type in implementations.Select(x => x.Type))
        {
            var id = type.FullName ?? "UnknownType";

            var instance = (ISourceType)ActivatorUtilities.CreateInstance(serviceProvider, type);

            if (instance.SettingsKeys != null
                && instance.SettingsKeys.Any(x => x.Key.StartsWith("_system", StringComparison.Ordinal)))
            {
                logger.LogWarning("Пропуск плагина '{PluginId}': содержит зарезервированный системный ключ настроек '_system'", id);
                continue;
            }

            if (MediaSources.TryAdd(id, instance))
            {
                logger.LogInformation("Плагин '{PluginId}' успешно инициализирован", id);
            }
            else
            {
                logger.LogError("Не удалось добавить плагин '{PluginId}'. Плагин с таким ID уже зарегистрирован", id);
            }
        }
    }
}
