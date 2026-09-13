namespace MediaOrcestrator.Runner.Wpf.Bootstrap.Gallery;

internal sealed record GalleryPage(string Key, string Title, string? Section = null);

internal static class GalleryPages
{
    public static IReadOnlyList<GalleryPage> All { get; } =
    [
        new("media", "Медиа"),
        new("sources", "Источники и связи"),
        new("audit", "Досинк источников"),
        new("transfers", "Передачи"),
        new("chronology", "Хронология"),
        new("publish", "Публикация"),
        new("rename", "Переименование"),
        new("preview", "Обложки и превью"),
        new("merge", "Слияние дубликатов"),
        new("conflicts", "Разбор конфликтов"),
        new("comments", "Комментарии"),
        new("system", "Система", "Задачи"),
        new("tools", "Система", "Инструменты"),
        new("logs", "Система", "Логи"),
        new("diagnostics", "Система", "Диагностика"),
        new("cleanup", "Система", "Очистка БД"),
        new("about", "Система", "О программе"),
        new("settings", "Система", "Настройки"),
    ];

    public static IReadOnlyList<string> AllKeys { get; } = [.. All.Select(page => page.Key)];

    public static GalleryPage? Find(string key)
    {
        return All.FirstOrDefault(page => string.Equals(page.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> Resolve(IReadOnlyList<string> requested, ICollection<string> unknown)
    {
        if (requested.Count == 0)
        {
            return AllKeys;
        }

        var keys = new List<string>();

        foreach (var item in requested)
        {
            if (Find(item) is not { } page)
            {
                unknown.Add(item);
                continue;
            }

            if (!keys.Contains(page.Key, StringComparer.Ordinal))
            {
                keys.Add(page.Key);
            }
        }

        return keys.Count > 0 ? keys : AllKeys;
    }
}
