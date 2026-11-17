using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MediaOrcestrator.Domain.Migration;

public sealed class WinFormsImportService(
    WinFormsInstallationInspector inspector,
    WinFormsSnapshotStager stager,
    IImportSettingsWriter settingsWriter,
    IShortcutCreator shortcutCreator,
    ILogger<WinFormsImportService> logger)
{
    public const string DefaultUpdateRepo = "TheVSAKeeper/MediaOrcestrator";
    public const string PendingManifestFileName = ".import-pending.json";
    public const string ResultFileName = ".import-result.json";
    public const string StagingRootFolderName = ".import-staging";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly HashSet<string> ExcludedAppDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "state",
        "temp",
        "backups",
        "logs",
        "shots",
        "gallery",
        "profiles",
        ".import-staging",
        "templates",
        "tools",
    };

    private static readonly HashSet<string> ExcludedExactRootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "settings.txt",
        "updater.log",
        ".import-pending.json",
        ".import-result.json",
    };

    public Task<WinFormsSeparateInstanceResult> CreateSeparateInstanceAsync(
        WinFormsSeparateInstanceOptions options,
        IProgress<WinFormsImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Task.Run(() => CreateSeparateInstanceCore(options, progress, cancellationToken), cancellationToken);
    }

    public Task<WinFormsReplaceCurrentResult> StageReplaceCurrentAsync(
        WinFormsReplaceCurrentOptions options,
        IProgress<WinFormsImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Task.Run(() => StageReplaceCurrentCore(options, progress, cancellationToken), cancellationToken);
    }

    private WinFormsSeparateInstanceResult CreateSeparateInstanceCore(
        WinFormsSeparateInstanceOptions options,
        IProgress<WinFormsImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new(WinFormsImportStage.Validating, "Проверка папок и доступности базы данных...", 5));

        var currentAppBase = WinFormsInstallationInspector.CanonicalizePath(
            string.IsNullOrWhiteSpace(options.CurrentAppBaseDirectory) ? AppContext.BaseDirectory : options.CurrentAppBaseDirectory);

        var inspection = inspector.Inspect(options.SourceFolder, currentAppBase);
        if (!inspection.IsValid || inspection.Settings is null)
        {
            throw new WinFormsImportException(
                WinFormsImportErrorCode.InvalidSource,
                inspection.ErrorMessage ?? "Папка WinForms не прошла проверку.");
        }

        if (inspection.ProcessStatus.IsBusy)
        {
            throw new WinFormsImportException(
                WinFormsImportErrorCode.SourceRunning,
                "Экземпляр WinForms сейчас запущен. Дождитесь закрытия процесса перед созданием копии.");
        }

        if (string.IsNullOrWhiteSpace(options.TargetFolder))
        {
            throw new WinFormsImportException(WinFormsImportErrorCode.TargetOverlap, "Укажите папку назначения для нового экземпляра WPF.");
        }

        var canonicalTarget = WinFormsInstallationInspector.CanonicalizePath(options.TargetFolder);

        if (WinFormsInstallationInspector.IsSameOrNestedPath(canonicalTarget, inspection.CanonicalPath)
            || WinFormsInstallationInspector.IsSameOrNestedPath(inspection.CanonicalPath, canonicalTarget))
        {
            throw new WinFormsImportException(
                WinFormsImportErrorCode.TargetOverlap,
                "Папка назначения не должна совпадать с папкой WinForms или находиться внутри неё.");
        }

        if (WinFormsInstallationInspector.IsSameOrNestedPath(canonicalTarget, currentAppBase)
            || WinFormsInstallationInspector.IsSameOrNestedPath(currentAppBase, canonicalTarget))
        {
            throw new WinFormsImportException(
                WinFormsImportErrorCode.TargetOverlap,
                "Папка назначения не должна совпадать с текущей папкой WPF или находиться внутри неё.");
        }

        EnsureNoDataPathOverlap(inspection.Settings, canonicalTarget);

        var targetExisted = Directory.Exists(canonicalTarget);
        if (targetExisted && Directory.EnumerateFileSystemEntries(canonicalTarget).Any())
        {
            throw new WinFormsImportException(
                WinFormsImportErrorCode.TargetNotEmpty,
                "Папка назначения не пуста. Выберите пустую или новую папку, чтобы не смешать данные.");
        }

        var stagingDir = Path.Combine(Path.GetTempPath(), "mo-import-stage-" + Guid.NewGuid().ToString("N"));
        var createdEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Directory.CreateDirectory(canonicalTarget);

            progress?.Report(new(WinFormsImportStage.StagingSnapshot, "Подготовка согласованного снимка базы и состояния...", 25));
            var finalCoversDir = Path.Combine(canonicalTarget, "templates", "covers");
            var staged = stager.StageSnapshot(inspection.Settings, stagingDir, finalCoversDir, cancellationToken);

            progress?.Report(new(WinFormsImportStage.CopyingApplication, "Копирование файлов приложения WPF и инструментов...", 55));
            CopyApplicationBinaries(currentAppBase, canonicalTarget, createdEntries, cancellationToken);
            CopyToolsIfAvailable(currentAppBase, inspection.CanonicalPath, canonicalTarget, createdEntries, cancellationToken);

            var targetDbPath = Path.Combine(canonicalTarget, "MyData.db");
            createdEntries.Add(targetDbPath);
            File.Copy(staged.DatabasePath, targetDbPath, overwrite: false);

            var targetStateDir = Path.Combine(canonicalTarget, "state");
            createdEntries.Add(targetStateDir);
            WinFormsSnapshotStager.CopyDirectoryRecursive(staged.StateDirectory, targetStateDir, overwrite: false, cancellationToken);

            if (Directory.Exists(staged.TemplatesCoversDirectory))
            {
                createdEntries.Add(Path.Combine(canonicalTarget, "templates"));
                WinFormsSnapshotStager.CopyDirectoryRecursive(staged.TemplatesCoversDirectory, finalCoversDir, overwrite: false, cancellationToken);
            }

            var targetTempDir = Path.Combine(canonicalTarget, "temp");
            createdEntries.Add(targetTempDir);
            Directory.CreateDirectory(targetTempDir);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(WinFormsImportStage.WritingSettings, "Создание settings.toml...", 80));
            var targetSettingsPath = Path.Combine(canonicalTarget, "settings.toml");
            var updateRepo = string.IsNullOrWhiteSpace(options.UpdateRepo) ? DefaultUpdateRepo : options.UpdateRepo.Trim();

            var settingsValues = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["plugin_path"] = "ModuleBuilds",
                ["database_path"] = "MyData.db",
                ["temp_path"] = "temp",
                ["state_path"] = "state",
                ["update_repo"] = updateRepo,
                ["wpf.sync.auto_enabled"] = options.EnableAutoSync ? "true" : "false",
                ["wpf.mcp.enabled"] = "false",
                ["wpf.import.completed"] = "true",
            };

            createdEntries.Add(targetSettingsPath);
            settingsWriter.WriteSettings(targetSettingsPath, settingsValues);

            var targetExe = Path.Combine(canonicalTarget, "MediaOrcestrator.Runner.Wpf.exe");
            string? shortcutPath = null;

            if (options.CreateShortcut)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(WinFormsImportStage.CreatingShortcut, "Создание ярлыка запуска...", 92));
                shortcutPath = CreateInstanceShortcut(canonicalTarget, targetExe, options);
            }

            progress?.Report(new(WinFormsImportStage.Completed, "Отдельный экземпляр WPF готов.", 100));
            logger.LogInformation("Создан отдельный экземпляр WPF из {Source} в {Target}", inspection.CanonicalPath, canonicalTarget);

            return new(
                canonicalTarget,
                targetExe,
                targetSettingsPath,
                targetDbPath,
                shortcutPath,
                staged.Summary,
                staged.Warnings);
        }
        catch
        {
            CleanupCreatedTarget(canonicalTarget, targetExisted, createdEntries);
            throw;
        }
        finally
        {
            WinFormsInstallationInspector.TryDeleteDirectory(stagingDir);
        }
    }

    private WinFormsReplaceCurrentResult StageReplaceCurrentCore(
        WinFormsReplaceCurrentOptions options,
        IProgress<WinFormsImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new(WinFormsImportStage.Validating, "Проверка папки WinForms и доступности базы...", 10));

        var targetAppBase = WinFormsInstallationInspector.CanonicalizePath(
            string.IsNullOrWhiteSpace(options.TargetAppBaseDirectory) ? AppContext.BaseDirectory : options.TargetAppBaseDirectory);

        var inspection = inspector.Inspect(options.SourceFolder, targetAppBase);
        if (!inspection.IsValid || inspection.Settings is null)
        {
            throw new WinFormsImportException(
                WinFormsImportErrorCode.InvalidSource,
                inspection.ErrorMessage ?? "Папка WinForms не прошла проверку.");
        }

        if (inspection.ProcessStatus.IsBusy)
        {
            throw new WinFormsImportException(
                WinFormsImportErrorCode.SourceRunning,
                "Экземпляр WinForms сейчас запущен. Дождитесь закрытия процесса перед заменой данных.");
        }

        var opId = DateTime.Now.ToString("yyyyMMdd_HHmmss_") + Guid.NewGuid().ToString("N")[..8];
        var stagingRoot = Path.Combine(targetAppBase, StagingRootFolderName);
        var opStagingDir = Path.Combine(stagingRoot, opId);

        var currentSettingsPath = Path.Combine(targetAppBase, "settings.toml");
        var targetDbPath = Path.Combine(targetAppBase, "MyData.db");
        var targetStatePath = Path.Combine(targetAppBase, "state");
        var targetCoversPath = Path.Combine(targetAppBase, "templates", "covers");

        var configuredDb = options.CurrentDatabasePath ?? settingsWriter.ReadValue(currentSettingsPath, "database_path");
        var previousDbPath = LegacySettingsParser.ResolvePath(targetAppBase, configuredDb, "MyData.db");

        var configuredState = options.CurrentStatePath ?? settingsWriter.ReadValue(currentSettingsPath, "state_path");
        var previousStatePath = LegacySettingsParser.ResolvePath(targetAppBase, configuredState, "state");

        EnsureNoDataPathOverlap(
            inspection.Settings,
            targetDbPath,
            targetStatePath,
            targetCoversPath,
            currentSettingsPath,
            previousDbPath,
            previousStatePath);

        try
        {
            progress?.Report(new(WinFormsImportStage.StagingSnapshot, "Подготовка снимка для замены данных...", 45));
            var staged = stager.StageSnapshot(inspection.Settings, opStagingDir, targetCoversPath, cancellationToken);

            progress?.Report(new(WinFormsImportStage.WritingSettings, "Подготовка конфигурации и манифеста отложенного применения...", 80));
            var stagedSettingsPath = Path.Combine(opStagingDir, "settings.toml");
            var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["plugin_path"] = "ModuleBuilds",
                ["database_path"] = "MyData.db",
                ["temp_path"] = "temp",
                ["state_path"] = "state",
                ["wpf.sync.auto_enabled"] = options.EnableAutoSync ? "true" : "false",
                ["wpf.import.completed"] = "true",
            };

            var existingRepo = settingsWriter.ReadValue(currentSettingsPath, "update_repo");
            if (string.IsNullOrWhiteSpace(existingRepo))
            {
                overrides["update_repo"] = string.IsNullOrWhiteSpace(options.UpdateRepo) ? DefaultUpdateRepo : options.UpdateRepo.Trim();
            }

            var preserveFrom = File.Exists(currentSettingsPath) ? currentSettingsPath : null;
            settingsWriter.WriteSettings(stagedSettingsPath, overrides, preserveFrom);

            if (options.CurrentDatabase is not null)
            {
                lock (options.CurrentDatabase)
                {
                    options.CurrentDatabase.Checkpoint();
                }
            }

            var backupsDir = Path.Combine(Path.GetDirectoryName(targetDbPath)!, "backups");
            var manifest = new WinFormsPendingImportManifest(
                opId,
                opStagingDir,
                inspection.CanonicalPath,
                staged.SourceDatabaseSha256,
                targetDbPath,
                targetStatePath,
                targetCoversPath,
                currentSettingsPath,
                previousDbPath,
                previousStatePath,
                backupsDir,
                staged.Summary,
                staged.Warnings,
                DateTimeOffset.UtcNow);

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            File.WriteAllText(Path.Combine(opStagingDir, "manifest.json"), manifestJson);

            cancellationToken.ThrowIfCancellationRequested();

            var pendingPath = Path.Combine(targetAppBase, PendingManifestFileName);
            var tempPendingPath = pendingPath + ".tmp";
            File.WriteAllText(tempPendingPath, manifestJson);
            File.Move(tempPendingPath, pendingPath, overwrite: true);

            progress?.Report(new(WinFormsImportStage.SchedulingRestart, "Снимок подготовлен, требуется перезапуск WPF...", 95));
            logger.LogInformation("Подготовлен отложенный импорт {OperationId} из {Source}", opId, inspection.CanonicalPath);

            var executablePath = Path.Combine(targetAppBase, "MediaOrcestrator.Runner.Wpf.exe");
            return new(
                pendingPath,
                executablePath,
                $"--apply-import={Environment.ProcessId}",
                manifest);
        }
        catch
        {
            WinFormsInstallationInspector.TryDeleteDirectory(opStagingDir);
            throw;
        }
    }

    private string CreateInstanceShortcut(
        string canonicalTarget,
        string targetExe,
        WinFormsSeparateInstanceOptions options)
    {
        var shortcutDir = string.IsNullOrWhiteSpace(options.ShortcutDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : options.ShortcutDirectory.Trim();

        var folderName = new DirectoryInfo(canonicalTarget).Name;
        var rawName = string.IsNullOrWhiteSpace(options.ShortcutName)
            ? $"MediaOrcestrator – {folderName}"
            : options.ShortcutName.Trim();

        var safeName = string.Concat(rawName.Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = $"MediaOrcestrator – {folderName}";
        }

        if (!safeName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            safeName += ".lnk";
        }

        var shortcutPath = Path.Combine(shortcutDir, safeName);
        shortcutCreator.CreateShortcut(
            shortcutPath,
            targetExe,
            canonicalTarget,
            $"MediaOrcestrator ({folderName})",
            targetExe,
            0);

        return shortcutPath;
    }

    public static void CancelPendingReplace(WinFormsReplaceCurrentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            if (File.Exists(result.PendingManifestPath))
            {
                File.Delete(result.PendingManifestPath);
            }
        }
        catch
        {
        }

        WinFormsInstallationInspector.TryDeleteDirectory(result.Manifest.StagingDirectory);
    }

    private static void EnsureNoDataPathOverlap(LegacySettingsSnapshot source, params string[] destinationPaths)
    {
        var sourcePaths = new[]
        {
            WinFormsInstallationInspector.CanonicalizePath(source.SourceFolder),
            WinFormsInstallationInspector.CanonicalizePath(source.DatabasePath),
            WinFormsInstallationInspector.CanonicalizePath(source.StatePath),
        };

        foreach (var destination in destinationPaths)
        {
            if (string.IsNullOrWhiteSpace(destination))
            {
                continue;
            }

            var canonicalDestination = WinFormsInstallationInspector.CanonicalizePath(destination);
            foreach (var canonicalSource in sourcePaths)
            {
                if (WinFormsInstallationInspector.IsSameOrNestedPath(canonicalDestination, canonicalSource)
                    || WinFormsInstallationInspector.IsSameOrNestedPath(canonicalSource, canonicalDestination))
                {
                    throw new WinFormsImportException(
                        WinFormsImportErrorCode.TargetOverlap,
                        $"Целевой путь {canonicalDestination} пересекается с исходными данными WinForms ({canonicalSource}). Перенос отменён.");
                }
            }
        }
    }

    private static void CopyApplicationBinaries(
        string sourceBaseDir,
        string targetDir,
        HashSet<string> createdEntries,
        CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(sourceBaseDir))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(file);
            if (IsExcludedRootFile(fileName))
            {
                continue;
            }

            var destination = Path.Combine(targetDir, fileName);
            createdEntries.Add(destination);
            File.Copy(file, destination, overwrite: false);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceBaseDir))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(dir);
            if (ExcludedAppDirectories.Contains(dirName))
            {
                continue;
            }

            var destinationSubDir = Path.Combine(targetDir, dirName);
            createdEntries.Add(destinationSubDir);
            WinFormsSnapshotStager.CopyDirectoryRecursive(dir, destinationSubDir, overwrite: false, cancellationToken);
        }
    }

    private static void CopyToolsIfAvailable(
        string currentAppBase,
        string sourceWinFormsDir,
        string targetDir,
        HashSet<string> createdEntries,
        CancellationToken cancellationToken)
    {
        var currentTools = Path.Combine(currentAppBase, "tools");
        var sourceTools = Path.Combine(sourceWinFormsDir, "tools");
        var chosenTools = Directory.Exists(currentTools)
            ? currentTools
            : Directory.Exists(sourceTools) ? sourceTools : null;

        if (chosenTools is null)
        {
            return;
        }

        var targetToolsDir = Path.Combine(targetDir, "tools");
        createdEntries.Add(targetToolsDir);
        WinFormsSnapshotStager.CopyDirectoryRecursive(chosenTools, targetToolsDir, overwrite: false, cancellationToken);
    }

    private static bool IsExcludedRootFile(string fileName)
    {
        if (ExcludedExactRootFiles.Contains(fileName))
        {
            return true;
        }

        if (fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".restore-pending", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("settings.toml", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("wpf-comments-view-settings.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static void CleanupCreatedTarget(string targetDir, bool targetExistedEmpty, HashSet<string> createdEntries)
    {
        try
        {
            if (!Directory.Exists(targetDir))
            {
                return;
            }

            if (!targetExistedEmpty)
            {
                Directory.Delete(targetDir, true);
                return;
            }

            foreach (var entry in createdEntries)
            {
                if (File.Exists(entry))
                {
                    File.Delete(entry);
                }
                else if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, true);
                }
            }
        }
        catch
        {
        }
    }
}
