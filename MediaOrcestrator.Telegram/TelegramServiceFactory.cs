using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediaOrcestrator.Telegram;

public interface ITelegramServiceFactory
{
    TelegramService Create(
        int apiId,
        string apiHash,
        string phoneNumber,
        string sessionPath);
}

public sealed class TelegramServiceFactory(
    IOptions<TelegramOptions> options,
    ILogger<TelegramService> logger)
    : ITelegramServiceFactory
{
    public TelegramService Create(
        int apiId,
        string apiHash,
        string phoneNumber,
        string sessionPath)
    {
        return new(apiId, apiHash, phoneNumber, sessionPath, options.Value, logger);
    }
}
