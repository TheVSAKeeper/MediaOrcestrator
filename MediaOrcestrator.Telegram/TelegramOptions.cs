namespace MediaOrcestrator.Telegram;

public sealed class TelegramOptions
{
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public int HistoryPageSize { get; set; } = 100;
}
