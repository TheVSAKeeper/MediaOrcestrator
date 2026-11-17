namespace MediaOrcestrator.Runner.Wpf.ViewModels.Settings;

public sealed record StartupPageOption(string Key, string Label)
{
    public override string ToString() => Label;
}

public static class SettingsOptions
{
    public static IReadOnlyList<StartupPageOption> StartupPages { get; } =
    [
        new(ShellPreferences.DefaultStartupSection, "Медиа"),
        new(ShellPreferences.LastUsedStartupSection, "Последняя открытая"),
        new("sources", "Источники и связи"),
        new("audit", "Досинк источников"),
        new("transfers", "Передачи"),
        new("chronology", "Хронология"),
        new("publish", "Публикация"),
        new("rename", "Операции – Переименование"),
        new("preview", "Операции – Обложки и превью"),
        new("merge", "Операции – Слияние дубликатов"),
        new("conflicts", "Операции – Разбор конфликтов"),
        new("comments", "Комментарии"),
        new("maintenance", "Обслуживание"),
        new("logs", "Логи"),
    ];

    public static bool IsValidStartupPage(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && StartupPages.Any(option => string.Equals(option.Key, key, StringComparison.Ordinal));

    public static string StartupPageLabelOf(string key) =>
        StartupPages.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.Ordinal))?.Label
        ?? StartupPages[0].Label;
}
