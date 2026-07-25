using LiteDB;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Domain;

public sealed class DatabaseMaintenanceService(
    LiteDatabase db,
    DatabaseBackupService backupService,
    string databasePath,
    string tempPath,
    string logsPath,
    string backupsPath,
    ILogger<DatabaseMaintenanceService> logger)
{
    private static readonly string[] DbCollections =
    [
        "media_comments",
        "medias",
        "source_relations",
        "sources",
    ];

    public IReadOnlyList<string> KnownCollections => DbCollections;

    public string DatabasePath { get; } = Path.GetFullPath(databasePath);

    public string TempPath { get; } = Path.GetFullPath(tempPath);

    public string LogsPath { get; } = Path.GetFullPath(logsPath);

    public string BackupsPath { get; } = Path.GetFullPath(backupsPath);

    public int CountCollection(string collectionName)
    {
        try
        {
            return db.GetCollection(collectionName).Count();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось получить размер коллекции {CollectionName}", collectionName);
            return 0;
        }
    }

    public int ClearCollection(string collectionName)
    {
        var collection = db.GetCollection(collectionName);
        var count = collection.Count();

        if (count == 0)
        {
            logger.LogInformation("Коллекция {CollectionName} уже пуста", collectionName);
            return 0;
        }

        collection.DeleteAll();
        logger.LogInformation("Коллекция {CollectionName}: удалено {Count} записей", collectionName, count);
        return count;
    }

    public long MeasureFolder(string path, bool recursive = true)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;

        try
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var file in Directory.EnumerateFiles(path, "*", option))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось измерить папку {Path}", path);
        }

        return total;
    }

    public int CountFiles(string path, bool recursive = true)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        try
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.EnumerateFiles(path, "*", option).Count();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось пересчитать файлы в {Path}", path);
            return 0;
        }
    }

    public long MeasureOldFiles(string path, TimeSpan? olderThan, params string[] patterns)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        var threshold = olderThan is { } age ? DateTime.UtcNow - age : (DateTime?)null;
        var effectivePatterns = patterns.Length == 0 ? ["*"] : patterns;
        long total = 0;

        foreach (var pattern in effectivePatterns)
        {
            foreach (var file in Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var info = new FileInfo(file);

                    if (threshold is null || info.LastWriteTimeUtc <= threshold.Value)
                    {
                        total += info.Length;
                    }
                }
                catch
                {
                }
            }
        }

        return total;
    }

    public (int Deleted, long FreedBytes) CleanFolder(string path, string searchPattern, TimeSpan? olderThan, bool recursive = false)
    {
        if (!Directory.Exists(path))
        {
            return (0, 0);
        }

        var threshold = olderThan is { } age ? DateTime.UtcNow - age : (DateTime?)null;
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var deleted = 0;
        long freed = 0;

        foreach (var file in Directory.EnumerateFiles(path, searchPattern, option))
        {
            try
            {
                var info = new FileInfo(file);

                if (threshold is not null && info.LastWriteTimeUtc > threshold.Value)
                {
                    continue;
                }

                var size = info.Length;
                File.Delete(file);
                deleted++;
                freed += size;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не удалось удалить файл {File}", file);
            }
        }

        logger.LogInformation("Папка {Path} ({Pattern}): удалено {Deleted} файлов, освобождено {Freed} байт",
            path, searchPattern, deleted, freed);

        return (deleted, freed);
    }

    public long MeasureDatabaseFile()
    {
        try
        {
            return File.Exists(DatabasePath) ? new FileInfo(DatabasePath).Length : 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось получить размер файла БД");
            return 0;
        }
    }

    public void RunPreCleanupBackup()
    {
        backupService.Backup(BackupTrigger.Manual);
    }
}
