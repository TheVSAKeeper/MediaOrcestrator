using LiteDB;
using Microsoft.Extensions.Logging;
using STJ = System.Text.Json;

namespace MediaOrcestrator.Domain;

// TODO: Механизмы удаления осознано опушены, так как требуют утверждения данжен-мастером
public class DatabaseBackupService(LiteDatabase db, string databasePath, ILogger<DatabaseBackupService> logger) : IDisposable
{
    private static readonly STJ.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _backupDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "backups");
    private readonly string _databaseName = Path.GetFileNameWithoutExtension(databasePath);
    private readonly object _logLock = new();

    private Timer? _timer;

    public string BackupDirectory => _backupDir;

    public string PendingRestoreMarkerPath => Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(databasePath))!,
        $"{_databaseName}.restore-pending");

    public string? GetPendingRestore()
    {
        try
        {
            if (!File.Exists(PendingRestoreMarkerPath))
            {
                return null;
            }

            var content = File.ReadAllText(PendingRestoreMarkerPath).Trim();
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось прочитать маркер восстановления БД");
            return null;
        }
    }

    public void PrepareRestore(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Имя файла бэкапа пустое", nameof(fileName));
        }

        var path = Path.Combine(_backupDir, fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Файл бэкапа не найден", path);
        }

        File.WriteAllText(PendingRestoreMarkerPath, fileName);
        logger.LogInformation("Запрошено восстановление БД из бэкапа {FileName} при следующем старте", fileName);
    }

    public void CancelPendingRestore()
    {
        try
        {
            if (File.Exists(PendingRestoreMarkerPath))
            {
                File.Delete(PendingRestoreMarkerPath);
                logger.LogInformation("Запрос на восстановление БД отменён");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось удалить маркер восстановления БД");
        }
    }

    public static string? ApplyPendingRestoreIfAny(string databasePath, ILogger logger)
    {
        var fullDbPath = Path.GetFullPath(databasePath);
        var dbDir = Path.GetDirectoryName(fullDbPath)!;
        var dbName = Path.GetFileNameWithoutExtension(fullDbPath);
        var markerPath = Path.Combine(dbDir, $"{dbName}.restore-pending");

        if (!File.Exists(markerPath))
        {
            return null;
        }

        string fileName;

        try
        {
            fileName = File.ReadAllText(markerPath).Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось прочитать маркер восстановления — пропускаю");
            TryDelete(markerPath, logger);
            return null;
        }

        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains(Path.DirectorySeparatorChar)
            || fileName.Contains(Path.AltDirectorySeparatorChar))
        {
            logger.LogWarning("Маркер восстановления повреждён (имя файла: {FileName}) — игнор", fileName);
            TryDelete(markerPath, logger);
            return null;
        }

        var backupsDir = Path.Combine(dbDir, "backups");
        var sourcePath = Path.Combine(backupsDir, fileName);

        if (!File.Exists(sourcePath))
        {
            logger.LogWarning("Бэкап для восстановления не найден: {SourcePath}", sourcePath);
            TryDelete(markerPath, logger);
            return null;
        }

        try
        {
            if (File.Exists(fullDbPath))
            {
                Directory.CreateDirectory(backupsDir);
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var preRestorePath = Path.Combine(backupsDir, $"{dbName}_{timestamp}_pre-restore.db");
                File.Copy(fullDbPath, preRestorePath, overwrite: false);
                logger.LogInformation("Текущая БД сохранена перед восстановлением: {Path}", preRestorePath);
            }

            File.Copy(sourcePath, fullDbPath, overwrite: true);
            logger.LogInformation("БД восстановлена из бэкапа: {FileName}", fileName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось восстановить БД из бэкапа {FileName}", fileName);
            return null;
        }
        finally
        {
            TryDelete(markerPath, logger);
        }

        return fileName;
    }

    private static void TryDelete(string path, ILogger logger)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось удалить файл: {Path}", path);
        }
    }

    public IReadOnlyList<BackupLogEntry> GetBackups()
    {
        lock (_logLock)
        {
            if (!Directory.Exists(_backupDir))
            {
                return [];
            }

            var entries = ReadLog();
            var alive = new List<BackupLogEntry>(entries.Count);
            var changed = false;

            foreach (var entry in entries)
            {
                var path = Path.Combine(_backupDir, entry.FileName);

                if (File.Exists(path))
                {
                    alive.Add(entry);
                }
                else
                {
                    changed = true;
                }
            }

            if (changed)
            {
                WriteLog(alive);
            }

            return alive
                .OrderByDescending(e => e.CreatedAt)
                .ToList();
        }
    }

    public bool DeleteBackup(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        lock (_logLock)
        {
            var path = Path.Combine(_backupDir, fileName);
            var existed = File.Exists(path);

            if (existed)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Не удалось удалить файл бэкапа: {Path}", path);
                    return false;
                }
            }

            var entries = ReadLog();
            var trimmed = entries.Where(e => !string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (trimmed.Count != entries.Count)
            {
                WriteLog(trimmed);
            }

            logger.LogInformation("Бэкап удалён: {FileName} (файл существовал: {Existed})", fileName, existed);
            return existed;
        }
    }

    public void Backup(BackupTrigger trigger)
    {
        try
        {
            Directory.CreateDirectory(_backupDir);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var triggerName = trigger.ToString().ToLowerInvariant();
            var backupFileName = $"{_databaseName}_{timestamp}_{triggerName}.db";
            var backupPath = Path.Combine(_backupDir, backupFileName);
            var fullDatabasePath = Path.GetFullPath(databasePath);

            lock (db)
            {
                db.Checkpoint();
                File.Copy(fullDatabasePath, backupPath);
            }

            var entry = new BackupLogEntry(backupFileName, DateTime.Now, trigger, new FileInfo(backupPath).Length);
            AppendToLog(entry);

            logger.LogInformation("Бэкап создан: {BackupFileName} ({SizeBytes} байт, триггер: {Trigger})",
                backupFileName, entry.SizeBytes, triggerName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при создании бэкапа базы данных (триггер: {Trigger})", trigger);
        }
    }

    public void ValidateLog()
    {
        lock (_logLock)
        {
            var logPath = Path.Combine(_backupDir, "backup-log.json");

            if (!File.Exists(logPath))
            {
                return;
            }

            var entries = ReadLog();
            var validEntries = entries.Where(e => File.Exists(Path.Combine(_backupDir, e.FileName))).ToList();

            if (validEntries.Count == entries.Count)
            {
                return;
            }

            logger.LogWarning("Удалено {Count} записей из журнала бэкапов (файлы не найдены)", entries.Count - validEntries.Count);
            WriteLog(validEntries);
        }
    }

    public void StartScheduled(TimeSpan interval)
    {
        _timer = new(_ => Backup(BackupTrigger.Scheduled), null, interval, interval);
        logger.LogInformation("Запланировано автоматическое резервное копирование каждые {Hours} ч", interval.TotalHours);
    }

    public void Dispose()
    {
        using var waitHandle = new ManualResetEvent(false);

        if (_timer != null)
        {
            _timer.Dispose(waitHandle);
            waitHandle.WaitOne();
        }

        GC.SuppressFinalize(this);
    }

    private List<BackupLogEntry> ReadLog()
    {
        var logPath = Path.Combine(_backupDir, "backup-log.json");

        if (!File.Exists(logPath))
        {
            return [];
        }

        var json = File.ReadAllText(logPath);
        return STJ.JsonSerializer.Deserialize<List<BackupLogEntry>>(json, JsonOptions) ?? [];
    }

    private void WriteLog(List<BackupLogEntry> entries)
    {
        var logPath = Path.Combine(_backupDir, "backup-log.json");
        var json = STJ.JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(logPath, json);
    }

    private void AppendToLog(BackupLogEntry entry)
    {
        lock (_logLock)
        {
            var entries = ReadLog();
            entries.Add(entry);
            WriteLog(entries);
        }
    }
}
