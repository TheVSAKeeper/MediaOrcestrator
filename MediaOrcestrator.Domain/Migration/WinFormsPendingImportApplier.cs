using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MediaOrcestrator.Domain.Migration;

public static class WinFormsPendingImportApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private const string ApplyStateFileName = "pending-apply-state.json";

    private sealed record PendingApplyState(string OperationId, string BackupDirectory);

    public static WinFormsPendingImportOutcome? ApplyIfPending(
        string baseDirectory,
        ILogger? logger = null,
        Action? faultHook = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var fullBase = Path.GetFullPath(baseDirectory);
        var pendingPath = Path.Combine(fullBase, WinFormsImportService.PendingManifestFileName);

        if (!File.Exists(pendingPath))
        {
            return null;
        }

        WinFormsPendingImportManifest? manifest;
        try
        {
            var json = File.ReadAllText(pendingPath);
            manifest = JsonSerializer.Deserialize<WinFormsPendingImportManifest>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Не удалось прочитать манифест отложенного импорта {Path}", pendingPath);
            var outcome = new WinFormsPendingImportOutcome(
                WinFormsPendingImportOutcomeStatus.Failed,
                "Файл задания отложенного импорта повреждён. Текущие данные не изменены.",
                ErrorMessage: ex.Message);

            WriteOutcomeAndCleanupPending(fullBase, pendingPath, null, outcome, logger);
            return outcome;
        }

        if (manifest is null
            || string.IsNullOrWhiteSpace(manifest.StagingDirectory)
            || !File.Exists(Path.Combine(manifest.StagingDirectory, "MyData.db"))
            || !File.Exists(Path.Combine(manifest.StagingDirectory, "settings.toml")))
        {
            logger?.LogWarning("Не найдены подготовленные файлы отложенного импорта {OperationId}", manifest?.OperationId);
            var outcome = new WinFormsPendingImportOutcome(
                WinFormsPendingImportOutcomeStatus.Failed,
                "Подготовленные файлы импорта не найдены. Текущие данные не изменены.",
                ErrorMessage: "Отсутствуют файлы в каталоге staging.");

            WriteOutcomeAndCleanupPending(fullBase, pendingPath, manifest?.StagingDirectory, outcome, logger);
            return outcome;
        }

        var existingState = TryReadApplyState(manifest.StagingDirectory);
        string backupDir;
        if (existingState is not null
            && string.Equals(existingState.OperationId, manifest.OperationId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(existingState.BackupDirectory)
            && Directory.Exists(existingState.BackupDirectory))
        {
            backupDir = existingState.BackupDirectory;
            logger?.LogWarning("Обнаружен незавершённый отложенный импорт {OperationId}, используется исходный бэкап {BackupDir}", manifest.OperationId, backupDir);
        }
        else
        {
            try
            {
                backupDir = CreatePreImportBackup(manifest, logger);
                WriteApplyState(manifest.StagingDirectory, new(manifest.OperationId, backupDir));
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Не удалось создать резервную копию перед применением импорта {OperationId}", manifest.OperationId);
                var outcome = new WinFormsPendingImportOutcome(
                    WinFormsPendingImportOutcomeStatus.Failed,
                    "Не удалось создать резервную копию текущих данных, поэтому замена отменена.",
                    ErrorMessage: ex.Message);

                WriteOutcomeAndCleanupPending(fullBase, pendingPath, manifest.StagingDirectory, outcome, logger);
                return outcome;
            }
        }

        try
        {
            ApplyCleanReplacement(manifest, faultHook);

            var successOutcome = new WinFormsPendingImportOutcome(
                WinFormsPendingImportOutcomeStatus.Applied,
                "Данные WinForms успешно перенесены в текущий экземпляр.",
                BackupDirectory: backupDir,
                Summary: manifest.Summary,
                Warnings: manifest.Warnings);

            WriteOutcomeAndCleanupPending(fullBase, pendingPath, manifest.StagingDirectory, successOutcome, logger);
            logger?.LogInformation("Отложенный импорт {OperationId} применён. Бэкап: {BackupDir}", manifest.OperationId, backupDir);
            return successOutcome;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Сбой применения импорта {OperationId}, выполняется откат из {BackupDir}", manifest.OperationId, backupDir);

            try
            {
                RollbackFromBackup(manifest, backupDir);
                logger?.LogInformation("Данные восстановлены после сбоя импорта из {BackupDir}", backupDir);
            }
            catch (Exception rollbackEx)
            {
                logger?.LogCritical(rollbackEx, "Критическая ошибка отката импорта из {BackupDir}", backupDir);
                return new WinFormsPendingImportOutcome(
                    WinFormsPendingImportOutcomeStatus.RollbackFailed,
                    $"Ошибка переноса и неполный откат. Закройте программы, удерживающие файлы, и повторите запуск. Резервная копия сохранена в: {backupDir}",
                    BackupDirectory: backupDir,
                    ErrorMessage: $"{ex.Message}; откат: {rollbackEx.Message}");
            }

            var rolledBackOutcome = new WinFormsPendingImportOutcome(
                WinFormsPendingImportOutcomeStatus.RolledBack,
                $"Перенос не удался ({ex.Message}). Прежние данные восстановлены из резервной копии.",
                BackupDirectory: backupDir,
                ErrorMessage: ex.Message);

            WriteOutcomeAndCleanupPending(fullBase, pendingPath, manifest.StagingDirectory, rolledBackOutcome, logger);
            return rolledBackOutcome;
        }
    }

    public static WinFormsPendingImportOutcome? TryReadAndConsumeResult(string baseDirectory, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var resultPath = Path.Combine(Path.GetFullPath(baseDirectory), WinFormsImportService.ResultFileName);
        if (!File.Exists(resultPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(resultPath);
            return JsonSerializer.Deserialize<WinFormsPendingImportOutcome>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Не удалось прочитать результат отложенного импорта {Path}", resultPath);
            return null;
        }
        finally
        {
            TryDeleteFile(resultPath, logger);
        }
    }

    private static string CreatePreImportBackup(WinFormsPendingImportManifest manifest, ILogger? logger)
    {
        Directory.CreateDirectory(manifest.BackupsDirectory);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var backupDir = Path.Combine(manifest.BackupsDirectory, $"pre-import_{timestamp}");
        var suffix = 1;
        while (Directory.Exists(backupDir))
        {
            backupDir = Path.Combine(manifest.BackupsDirectory, $"pre-import_{timestamp}_{suffix}");
            suffix++;
        }

        Directory.CreateDirectory(backupDir);

        if (File.Exists(manifest.TargetSettingsPath))
        {
            File.Copy(manifest.TargetSettingsPath, Path.Combine(backupDir, "settings.toml"), overwrite: true);
        }

        BackupDatabasePair(manifest.PreviousDatabasePath, Path.Combine(backupDir, "MyData.db"));

        if (!PathsEqual(manifest.PreviousDatabasePath, manifest.TargetDatabasePath))
        {
            BackupDatabasePair(manifest.TargetDatabasePath, Path.Combine(backupDir, "target_MyData.db"));
        }

        if (Directory.Exists(manifest.PreviousStatePath))
        {
            WinFormsSnapshotStager.CopyDirectoryRecursive(manifest.PreviousStatePath, Path.Combine(backupDir, "state"), overwrite: true);
        }

        if (!PathsEqual(manifest.PreviousStatePath, manifest.TargetStatePath) && Directory.Exists(manifest.TargetStatePath))
        {
            WinFormsSnapshotStager.CopyDirectoryRecursive(manifest.TargetStatePath, Path.Combine(backupDir, "target_state"), overwrite: true);
        }

        if (Directory.Exists(manifest.TargetTemplatesCoversPath))
        {
            WinFormsSnapshotStager.CopyDirectoryRecursive(manifest.TargetTemplatesCoversPath, Path.Combine(backupDir, "templates", "covers"), overwrite: true);
        }

        RegisterDatabaseBackupCard(manifest, timestamp, logger);
        return backupDir;
    }

    private static void RegisterDatabaseBackupCard(WinFormsPendingImportManifest manifest, string timestamp, ILogger? logger)
    {
        var dbToArchive = File.Exists(manifest.PreviousDatabasePath)
            ? manifest.PreviousDatabasePath
            : File.Exists(manifest.TargetDatabasePath) ? manifest.TargetDatabasePath : null;

        if (dbToArchive is null)
        {
            return;
        }

        try
        {
            var dbName = Path.GetFileNameWithoutExtension(dbToArchive);
            var backupFileName = $"{dbName}_{timestamp}_pre-import.db";
            var backupDbPath = Path.Combine(manifest.BackupsDirectory, backupFileName);
            var suffix = 1;

            while (File.Exists(backupDbPath))
            {
                backupFileName = $"{dbName}_{timestamp}_{suffix}_pre-import.db";
                backupDbPath = Path.Combine(manifest.BackupsDirectory, backupFileName);
                suffix++;
            }

            File.Copy(dbToArchive, backupDbPath, overwrite: false);

            var logPath = Path.Combine(manifest.BackupsDirectory, "backup-log.json");
            var entries = File.Exists(logPath)
                ? JsonSerializer.Deserialize<List<BackupLogEntry>>(File.ReadAllText(logPath), JsonOptions) ?? []
                : [];

            entries.Add(new(backupFileName, DateTime.Now, BackupTrigger.Manual, new FileInfo(backupDbPath).Length));
            File.WriteAllText(logPath, JsonSerializer.Serialize(entries, JsonOptions));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Не удалось зарегистрировать pre-import бэкап в backup-log.json");
        }
    }

    private static void ApplyCleanReplacement(WinFormsPendingImportManifest manifest, Action? faultHook)
    {
        DeleteDatabaseAndMarkersStrict(manifest.TargetDatabasePath);
        if (!PathsEqual(manifest.PreviousDatabasePath, manifest.TargetDatabasePath))
        {
            DeleteDatabaseAndMarkersStrict(manifest.PreviousDatabasePath);
        }

        DeleteDirectoryStrict(manifest.TargetStatePath);
        DeleteDirectoryStrict(manifest.TargetTemplatesCoversPath);
        DeleteFileStrict(manifest.TargetSettingsPath);

        var targetDbDir = Path.GetDirectoryName(manifest.TargetDatabasePath);
        if (!string.IsNullOrWhiteSpace(targetDbDir))
        {
            Directory.CreateDirectory(targetDbDir);
        }

        File.Copy(Path.Combine(manifest.StagingDirectory, "MyData.db"), manifest.TargetDatabasePath, overwrite: true);

        faultHook?.Invoke();

        var stagedStateDir = Path.Combine(manifest.StagingDirectory, "state");
        if (Directory.Exists(stagedStateDir))
        {
            WinFormsSnapshotStager.CopyDirectoryRecursive(stagedStateDir, manifest.TargetStatePath, overwrite: true);
        }
        else
        {
            Directory.CreateDirectory(manifest.TargetStatePath);
        }

        var stagedCoversDir = Path.Combine(manifest.StagingDirectory, "templates", "covers");
        if (Directory.Exists(stagedCoversDir))
        {
            WinFormsSnapshotStager.CopyDirectoryRecursive(stagedCoversDir, manifest.TargetTemplatesCoversPath, overwrite: true);
        }

        File.Copy(Path.Combine(manifest.StagingDirectory, "settings.toml"), manifest.TargetSettingsPath, overwrite: true);
    }

    private static void RollbackFromBackup(WinFormsPendingImportManifest manifest, string backupDir)
    {
        DeleteDatabaseAndMarkersStrict(manifest.TargetDatabasePath);
        if (!PathsEqual(manifest.PreviousDatabasePath, manifest.TargetDatabasePath))
        {
            DeleteDatabaseAndMarkersStrict(manifest.PreviousDatabasePath);
        }

        DeleteDirectoryStrict(manifest.TargetStatePath);
        if (!PathsEqual(manifest.PreviousStatePath, manifest.TargetStatePath))
        {
            DeleteDirectoryStrict(manifest.PreviousStatePath);
        }

        DeleteDirectoryStrict(manifest.TargetTemplatesCoversPath);
        DeleteFileStrict(manifest.TargetSettingsPath);

        var backupSettings = Path.Combine(backupDir, "settings.toml");
        if (File.Exists(backupSettings))
        {
            File.Copy(backupSettings, manifest.TargetSettingsPath, overwrite: true);
        }

        RestoreDatabasePair(Path.Combine(backupDir, "MyData.db"), manifest.PreviousDatabasePath);
        if (!PathsEqual(manifest.PreviousDatabasePath, manifest.TargetDatabasePath))
        {
            RestoreDatabasePair(Path.Combine(backupDir, "target_MyData.db"), manifest.TargetDatabasePath);
        }

        var backupState = Path.Combine(backupDir, "state");
        if (Directory.Exists(backupState))
        {
            WinFormsSnapshotStager.CopyDirectoryRecursive(backupState, manifest.PreviousStatePath, overwrite: true);
        }

        var backupTargetState = Path.Combine(backupDir, "target_state");
        if (!PathsEqual(manifest.PreviousStatePath, manifest.TargetStatePath) && Directory.Exists(backupTargetState))
        {
            WinFormsSnapshotStager.CopyDirectoryRecursive(backupTargetState, manifest.TargetStatePath, overwrite: true);
        }

        var backupCovers = Path.Combine(backupDir, "templates", "covers");
        if (Directory.Exists(backupCovers))
        {
            WinFormsSnapshotStager.CopyDirectoryRecursive(backupCovers, manifest.TargetTemplatesCoversPath, overwrite: true);
        }
    }

    private static void BackupDatabasePair(string sourceDbPath, string backupDbPath)
    {
        var sourceWal = WinFormsProcessDetector.GetWalLogPath(sourceDbPath);
        var sourceMarker = GetRestorePendingMarkerPath(sourceDbPath);

        if (File.Exists(sourceDbPath))
        {
            File.Copy(sourceDbPath, backupDbPath, overwrite: true);
        }

        if (File.Exists(sourceWal))
        {
            File.Copy(sourceWal, WinFormsProcessDetector.GetWalLogPath(backupDbPath), overwrite: true);
        }

        if (sourceMarker is not null && File.Exists(sourceMarker))
        {
            var backupMarker = GetRestorePendingMarkerPath(backupDbPath);
            if (backupMarker is not null)
            {
                File.Copy(sourceMarker, backupMarker, overwrite: true);
            }
        }
    }

    private static void RestoreDatabasePair(string backupDbPath, string targetDbPath)
    {
        var backupWal = WinFormsProcessDetector.GetWalLogPath(backupDbPath);
        var backupMarker = GetRestorePendingMarkerPath(backupDbPath);

        if (!File.Exists(backupDbPath)
            && !File.Exists(backupWal)
            && (backupMarker is null || !File.Exists(backupMarker)))
        {
            return;
        }

        var dir = Path.GetDirectoryName(targetDbPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(backupDbPath))
        {
            File.Copy(backupDbPath, targetDbPath, overwrite: true);
        }

        if (File.Exists(backupWal))
        {
            File.Copy(backupWal, WinFormsProcessDetector.GetWalLogPath(targetDbPath), overwrite: true);
        }

        if (backupMarker is not null && File.Exists(backupMarker))
        {
            var targetMarker = GetRestorePendingMarkerPath(targetDbPath);
            if (targetMarker is not null)
            {
                File.Copy(backupMarker, targetMarker, overwrite: true);
            }
        }
    }

    private static void DeleteDatabaseAndMarkersStrict(string dbPath)
    {
        DeleteFileStrict(dbPath);
        DeleteFileStrict(WinFormsProcessDetector.GetWalLogPath(dbPath));

        var markerPath = GetRestorePendingMarkerPath(dbPath);
        if (markerPath is not null)
        {
            DeleteFileStrict(markerPath);
        }
    }

    private static string? GetRestorePendingMarkerPath(string dbPath)
    {
        var dbDir = Path.GetDirectoryName(dbPath);
        var dbName = Path.GetFileNameWithoutExtension(dbPath);
        if (string.IsNullOrWhiteSpace(dbDir) || string.IsNullOrWhiteSpace(dbName))
        {
            return null;
        }

        return Path.Combine(dbDir, $"{dbName}.restore-pending");
    }

    private static void WriteOutcomeAndCleanupPending(
        string baseDirectory,
        string pendingPath,
        string? stagingDirectory,
        WinFormsPendingImportOutcome outcome,
        ILogger? logger)
    {
        try
        {
            var resultPath = Path.Combine(baseDirectory, WinFormsImportService.ResultFileName);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(outcome, JsonOptions));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Не удалось записать результат импорта");
        }

        TryDeleteFile(pendingPath, logger);

        if (!string.IsNullOrWhiteSpace(stagingDirectory))
        {
            WinFormsInstallationInspector.TryDeleteDirectory(stagingDirectory);
        }
    }

    private static PendingApplyState? TryReadApplyState(string stagingDirectory)
    {
        var statePath = Path.Combine(stagingDirectory, ApplyStateFileName);
        if (!File.Exists(statePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(statePath);
            return JsonSerializer.Deserialize<PendingApplyState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteApplyState(string stagingDirectory, PendingApplyState state)
    {
        var statePath = Path.Combine(stagingDirectory, ApplyStateFileName);
        var tempPath = statePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(tempPath, statePath, overwrite: true);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteFileStrict(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        ClearReadOnlyAttribute(path);
        File.Delete(path);
    }

    private static void DeleteDirectoryStrict(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            ClearReadOnlyAttribute(file);
        }

        Directory.Delete(path, true);
    }

    private static void ClearReadOnlyAttribute(string filePath)
    {
        try
        {
            var attributes = File.GetAttributes(filePath);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path, ILogger? logger = null)
    {
        try
        {
            DeleteFileStrict(path);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Не удалось удалить файл {Path}", path);
        }
    }
}
