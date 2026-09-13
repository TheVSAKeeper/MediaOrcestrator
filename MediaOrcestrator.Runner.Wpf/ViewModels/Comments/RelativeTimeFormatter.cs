namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

internal static class RelativeTimeFormatter
{
    public static string Format(DateTime publishedAtUtc, DateTime nowUtc, string absoluteFallback)
    {
        var diffSeconds = (long)(nowUtc - publishedAtUtc).TotalSeconds;

        return diffSeconds switch
        {
            < 5 => "только что",
            < 60 => $"{diffSeconds} сек. назад",
            < 3600 => $"{diffSeconds / 60} мин. назад",
            < 86400 => $"{diffSeconds / 3600} ч. назад",
            < 2592000 => $"{diffSeconds / 86400} дн. назад",
            < 31536000 => $"{diffSeconds / 2592000} мес. назад",
            _ => absoluteFallback,
        };
    }
}
