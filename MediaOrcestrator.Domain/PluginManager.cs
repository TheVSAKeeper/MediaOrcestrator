using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Domain;

public class PluginManager(
    IEnumerable<ISourceType> sources,
    ToolManager toolManager,
    ILogger<PluginManager> logger)
{
    public Dictionary<string, ISourceType> MediaSources { get; private set; } = new();

    public void Init()
    {
        MediaSources = new();

        foreach (var instance in sources)
        {
            var id = instance.GetType().FullName ?? "UnknownType";

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

        var toolConsumers = MediaSources.Values.OfType<IToolConsumer>().ToList();

        if (toolConsumers.Count <= 0)
        {
            return;
        }

        toolManager.RegisterTools(toolConsumers);
        toolManager.ResolveAll();
    }
}
