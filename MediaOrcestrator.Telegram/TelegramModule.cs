using MediaOrcestrator.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace MediaOrcestrator.Telegram;

public sealed class TelegramModule : IPluginModule
{
    public void Register(IServiceCollection services)
    {
        services.AddOptions<TelegramOptions>();
        services.AddSingleton<ITelegramServiceFactory, TelegramServiceFactory>();
        services.AddSingleton<ISourceType, TelegramChannel>();
    }
}
