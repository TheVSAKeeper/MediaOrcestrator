using LiteDB;

namespace MediaOrcestrator.HardDiskDrive;

internal static class HardDiskDriveStore
{
    public static (string BasePath, string DbPath) ResolveDbPath(Dictionary<string, string> settings)
    {
        var basePath = settings["path"];
        var dbFileName = settings.GetValueOrDefault("dbFileName", "data.db");
        return (basePath, Path.Combine(basePath, dbFileName));
    }

    // TODO: Костыль Connection=shared
    public static LiteDatabase OpenDatabase(string dbPath)
    {
        return new($"Filename={dbPath};Connection=shared");
    }

    public static LiteDatabase? TryOpen(
        Dictionary<string, string> settings,
        out string basePath)
    {
        var (resolvedBase, dbPath) = ResolveDbPath(settings);
        basePath = resolvedBase;
        return Directory.Exists(basePath) && File.Exists(dbPath) ? OpenDatabase(dbPath) : null;
    }

    public static LiteDatabase OpenOrThrow(
        Dictionary<string, string> settings,
        out string basePath)
    {
        var (resolvedBase, dbPath) = ResolveDbPath(settings);
        basePath = resolvedBase;

        if (!File.Exists(dbPath))
        {
            throw new FileNotFoundException("База данных не найдена", dbPath);
        }

        return OpenDatabase(dbPath);
    }

    public static string ResolveSourceFilePath(
        string externalId,
        Dictionary<string, string> settings)
    {
        using var db = OpenOrThrow(settings, out var basePath);
        var file = db.GetCollection<DriveMedia>("files").FindById(externalId);

        if (file == null)
        {
            throw new InvalidOperationException($"Медиа {externalId} не найдено в базе данных");
        }

        var fullPath = Path.Combine(basePath, file.Id, file.Path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Исходный файл не найден", fullPath);
        }

        return fullPath;
    }
}
