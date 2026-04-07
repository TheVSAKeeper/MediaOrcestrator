using LiteDB;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Domain;

public sealed class TempManager(string tempPath, LiteDatabase db, ILogger<TempManager> logger)
{
    public string TempPath => tempPath;

    public void MigrateOldTempPaths()
    {
        var sources = db.GetCollection<Source>("sources").FindAll().ToList();
        var oldPaths = sources
            .Select(s => s.Settings.GetValueOrDefault("temp_path"))
            .Where(p => p != null && !string.Equals(p, tempPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var oldPath in oldPaths)
        {
            logger.LogInformation("Миграция: очистка старой временной папки {OldPath}", oldPath);
            CleanDirectory(oldPath!);
        }

        var updated = false;

        foreach (var source in sources)
        {
            if (source.Settings.Remove("temp_path"))
            {
                updated = true;
            }
        }

        if (updated)
        {
            var collection = db.GetCollection<Source>("sources");

            foreach (var source in sources)
            {
                collection.Update(source);
            }

            logger.LogInformation("Миграция: удалён ключ temp_path из настроек {Count} источников", sources.Count);
        }

        CleanVkVideoTempFiles();
    }

    public void CleanAll()
    {
        CleanDirectory(tempPath);
    }

    public void CleanMedia(string guid)
    {
        var path = Path.Combine(tempPath, guid);
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            var size = GetDirectorySize(path);
            Directory.Delete(path, true);
            logger.LogInformation("Очищена временная директория медиа: {Path} ({Size} байт)", path, size);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось очистить временную директорию медиа: {Path}", path);
        }
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    private void CleanDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var entries = Directory.GetFileSystemEntries(path);
        if (entries.Length == 0)
        {
            return;
        }

        logger.LogInformation("Очистка временной папки {TempPath}: {Count} элементов", path, entries.Length);

        var deletedCount = 0;
        long totalSize = 0;

        foreach (var entry in entries)
        {
            try
            {
                if (File.Exists(entry))
                {
                    var size = new FileInfo(entry).Length;
                    File.Delete(entry);
                    totalSize += size;
                    deletedCount++;
                    logger.LogDebug("Удалён файл: {Path}", entry);
                }
                else if (Directory.Exists(entry))
                {
                    var size = GetDirectorySize(entry);
                    Directory.Delete(entry, true);
                    totalSize += size;
                    deletedCount++;
                    logger.LogDebug("Удалена директория: {Path}", entry);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не удалось удалить {Path}", entry);
            }
        }

        logger.LogInformation("Очистка завершена: удалено {Count} элементов, освобождено {Size} байт", deletedCount, totalSize);
    }

    private void CleanVkVideoTempFiles()
    {
        var tempDir = Path.GetTempPath();

        try
        {
            var vkFiles = Directory.GetFiles(tempDir, "vkvideo_*");
            if (vkFiles.Length == 0)
            {
                return;
            }

            logger.LogInformation("Миграция: найдено {Count} старых файлов VK Video в {TempDir}", vkFiles.Length, tempDir);

            foreach (var file in vkFiles)
            {
                try
                {
                    var size = new FileInfo(file).Length;
                    File.Delete(file);
                    logger.LogDebug("Удалён старый файл VK Video: {Path} ({Size} байт)", file, size);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Не удалось удалить {Path}", file);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось очистить старые файлы VK Video в {TempDir}", tempDir);
        }
    }
}
