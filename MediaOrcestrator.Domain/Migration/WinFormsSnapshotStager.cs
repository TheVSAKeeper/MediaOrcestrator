using LiteDB;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MediaOrcestrator.Domain.Migration;

public sealed class WinFormsSnapshotStager(IWinFormsProcessDetector processDetector)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly LegacyAuthMapping[] LegacyAuthMappings =
    [
        new("Rutube", "auth_state_path", LegacyAuthKind.SingleFile, "auth_state"),
        new("Youtube", "auth_state_path", LegacyAuthKind.SingleFile, "auth_state"),
        new("Youtube", "token_path", LegacyAuthKind.GoogleOAuthDir, "oauth"),
        new("VkVideo", "auth_state_path", LegacyAuthKind.SingleFile, "auth_state"),
        new("Telegram", "session_path", LegacyAuthKind.SingleFile, "telegram.session"),
    ];

    private enum LegacyAuthKind
    {
        None = 0,
        SingleFile = 1,
        GoogleOAuthDir = 2,
    }

    public WinFormsStagedSnapshot StageSnapshot(
        LegacySettingsSnapshot settings,
        string stagingDirectory,
        string finalTemplatesCoversDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalTemplatesCoversDirectory);

        cancellationToken.ThrowIfCancellationRequested();

        var running = processDetector.CheckRunning(settings.SourceFolder, settings.DatabasePath);
        if (running.IsBusy)
        {
            throw new WinFormsImportException(
                WinFormsImportErrorCode.SourceRunning,
                "Экземпляр WinForms сейчас запущен или удерживает файлы базы данных. Закройте его перед переносом.");
        }

        Directory.CreateDirectory(stagingDirectory);

        var walPath = WinFormsProcessDetector.GetWalLogPath(settings.DatabasePath);
        var hasWal = File.Exists(walPath);

        var dbSize = new FileInfo(settings.DatabasePath).Length;
        var walSize = hasWal ? new FileInfo(walPath).Length : 0L;

        var dbShaBefore = FileIntegrity.ComputeSha256(settings.DatabasePath);
        var walShaBefore = hasWal ? FileIntegrity.ComputeSha256(walPath) : null;

        var stagedDbPath = Path.Combine(stagingDirectory, "MyData.db");
        var stagedWalPath = WinFormsProcessDetector.GetWalLogPath(stagedDbPath);

        cancellationToken.ThrowIfCancellationRequested();
        File.Copy(settings.DatabasePath, stagedDbPath, overwrite: true);

        if (hasWal)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(walPath, stagedWalPath, overwrite: true);
        }

        var hasWalAfter = File.Exists(walPath);
        var dbShaAfter = FileIntegrity.ComputeSha256(settings.DatabasePath);
        var walShaAfter = hasWalAfter ? FileIntegrity.ComputeSha256(walPath) : null;

        if (hasWal != hasWalAfter
            || !string.Equals(dbShaBefore, dbShaAfter, StringComparison.Ordinal)
            || !string.Equals(walShaBefore, walShaAfter, StringComparison.Ordinal)
            || processDetector.CheckRunning(settings.SourceFolder, settings.DatabasePath).IsBusy)
        {
            throw new WinFormsImportException(
                WinFormsImportErrorCode.SourceChangedDuringCopy,
                "Исходная база данных WinForms изменилась во время копирования. Остановите WinForms и повторите попытку.");
        }

        var stagedStateDir = Path.Combine(stagingDirectory, "state");
        Directory.CreateDirectory(stagedStateDir);

        if (Directory.Exists(settings.StatePath))
        {
            CopyDirectoryRecursive(settings.StatePath, stagedStateDir, overwrite: true, cancellationToken);
        }

        var sourceCoversDir = Path.Combine(settings.SourceFolder, "templates", "covers");
        var stagedCoversDir = Path.Combine(stagingDirectory, "templates", "covers");
        var coversCount = CopyAndRebindCoverTemplates(
            settings.SourceFolder,
            sourceCoversDir,
            stagedCoversDir,
            finalTemplatesCoversDirectory,
            cancellationToken);

        var warnings = new List<WinFormsImportWarning>();
        var hardDiskPaths = new List<string>();

        if (hasWal)
        {
            warnings.Add(new(
                WinFormsImportWarningKind.PendingWalLog,
                string.Empty,
                string.Empty,
                $"Журнал {Path.GetFileName(walPath)} проигран в перенесённую базу."));
        }

        int sourcesCount;
        int mediasCount;
        int linksCount;
        int relationsCount;

        using (var db = new LiteDatabase(stagedDbPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            MediaLinkBsonMigrator.BackfillMissingFields(db);

            var sourcesCollection = db.GetCollection<Source>("sources");
            var sources = sourcesCollection.FindAll().ToList();

            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var dirty = NormalizeSourceSettings(
                    source,
                    settings,
                    stagedStateDir,
                    hardDiskPaths,
                    warnings);

                if (dirty)
                {
                    sourcesCollection.Update(source);
                }
            }

            sourcesCount = sources.Count;
            var mediaDocs = db.GetCollection("medias").FindAll().ToList();
            mediasCount = mediaDocs.Count;
            linksCount = mediaDocs.Sum(doc => doc.TryGetValue("Sources", out var arr) && arr.IsArray ? arr.AsArray.Count : 0);
            relationsCount = db.GetCollection("source_relations").Count();

            db.Checkpoint();
        }

        if (File.Exists(stagedWalPath))
        {
            File.Delete(stagedWalPath);
        }

        var summary = new WinFormsDatabaseSummary(
            sourcesCount,
            mediasCount,
            linksCount,
            relationsCount,
            coversCount,
            hasWal,
            dbSize,
            walSize,
            hardDiskPaths,
            warnings);

        return new(
            stagingDirectory,
            stagedDbPath,
            stagedStateDir,
            stagedCoversDir,
            dbShaBefore,
            walShaBefore,
            summary,
            warnings);
    }

    internal static void CollectSourceInspectionWarnings(
        Source source,
        LegacySettingsSnapshot settings,
        List<string> hardDiskPaths,
        List<WinFormsImportWarning> warnings)
    {
        if (string.Equals(source.TypeId, "HardDiskDrive", StringComparison.OrdinalIgnoreCase)
            && source.Settings.TryGetValue("path", out var hddPath)
            && !string.IsNullOrWhiteSpace(hddPath))
        {
            var resolved = ResolveFromSourceFolder(settings.SourceFolder, hddPath);
            hardDiskPaths.Add(resolved);
            warnings.Add(new(
                WinFormsImportWarningKind.ExternalHardDiskPath,
                source.Id,
                source.Title,
                $"Источник «{source.Title}» привязан к каталогу HDD {resolved}. Не запускайте запись в общий каталог из WinForms и WPF одновременно."));
        }

        if (source.Settings.TryGetValue("_system_state_path", out var customState)
            && !string.IsNullOrWhiteSpace(customState))
        {
            var resolvedCustom = WinFormsInstallationInspector.CanonicalizePath(ResolveFromSourceFolder(settings.SourceFolder, customState));
            var defaultSourceState = WinFormsInstallationInspector.CanonicalizePath(Path.Combine(settings.StatePath, source.Id));

            if (!string.Equals(resolvedCustom, defaultSourceState, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(new(
                    WinFormsImportWarningKind.CustomSourceStatePath,
                    source.Id,
                    source.Title,
                    $"Источник «{source.Title}» ссылается на отдельный каталог состояния {resolvedCustom} – файлы будут скопированы в изолированное состояние."));
            }
        }

        foreach (var mapping in LegacyAuthMappings)
        {
            if (!string.Equals(mapping.TypeId, source.TypeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!source.Settings.TryGetValue(mapping.LegacyKey, out var legacyValue) || string.IsNullOrWhiteSpace(legacyValue))
            {
                continue;
            }

            var resolved = ResolveFromSourceFolder(settings.SourceFolder, legacyValue);

            if (mapping.Kind == LegacyAuthKind.SingleFile)
            {
                if (!File.Exists(resolved))
                {
                    warnings.Add(new(
                        WinFormsImportWarningKind.MissingExternalSessionFile,
                        source.Id,
                        source.Title,
                        $"Файл сессии источника «{source.Title}» ({mapping.LegacyKey}) не найден: {resolved}. Потребуется повторная авторизация."));
                }
                else if (IsYoutubeLegacyJsonFormat(source.TypeId, resolved))
                {
                    warnings.Add(new(
                        WinFormsImportWarningKind.LegacyYoutubePlaywrightAuth,
                        source.Id,
                        source.Title,
                        $"У источника «{source.Title}» найден старый Playwright JSON ({resolved}). В WPF потребуется войти в YouTube заново."));
                }
            }
            else if (mapping.Kind == LegacyAuthKind.GoogleOAuthDir)
            {
                var dir = Path.GetDirectoryName(resolved);
                var hasTokens = !string.IsNullOrWhiteSpace(dir)
                    && Directory.Exists(dir)
                    && Directory.EnumerateFiles(dir, "Google.Apis.Auth.OAuth2.Responses.*").Any();

                if (!hasTokens)
                {
                    warnings.Add(new(
                        WinFormsImportWarningKind.MissingExternalSessionFile,
                        source.Id,
                        source.Title,
                        $"Файлы OAuth-токенов источника «{source.Title}» не найдены рядом с {resolved}."));
                }
            }
        }
    }

    internal static void CopyDirectoryRecursive(
        string sourceDir,
        string targetDir,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destination, overwrite);
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, destinationSubDir, overwrite, cancellationToken);
        }
    }

    private static bool NormalizeSourceSettings(
        Source source,
        LegacySettingsSnapshot settings,
        string stagedStateDir,
        List<string> hardDiskPaths,
        List<WinFormsImportWarning> warnings)
    {
        var dirty = false;
        var targetSourceStateDir = Path.Combine(stagedStateDir, source.Id);

        if (source.Settings.TryGetValue("_system_state_path", out var customStatePath)
            && !string.IsNullOrWhiteSpace(customStatePath))
        {
            var resolvedCustom = ResolveFromSourceFolder(settings.SourceFolder, customStatePath);
            var canonicalCustom = WinFormsInstallationInspector.CanonicalizePath(resolvedCustom);
            var canonicalDefault = WinFormsInstallationInspector.CanonicalizePath(Path.Combine(settings.StatePath, source.Id));

            if (!string.Equals(canonicalCustom, canonicalDefault, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(canonicalCustom))
            {
                CopyDirectoryRecursive(canonicalCustom, targetSourceStateDir, overwrite: true);
                warnings.Add(new(
                    WinFormsImportWarningKind.CustomSourceStatePath,
                    source.Id,
                    source.Title,
                    $"Отдельный каталог состояния источника «{source.Title}» скопирован из {canonicalCustom}."));
            }
        }

        if (source.Settings.Remove("temp_path"))
        {
            dirty = true;
        }

        if (source.Settings.Remove("_system_temp_path"))
        {
            dirty = true;
        }

        if (source.Settings.Remove("_system_state_path"))
        {
            dirty = true;
        }

        foreach (var mapping in LegacyAuthMappings)
        {
            if (!string.Equals(mapping.TypeId, source.TypeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!source.Settings.TryGetValue(mapping.LegacyKey, out var legacyValue) || string.IsNullOrWhiteSpace(legacyValue))
            {
                continue;
            }

            var resolved = ResolveFromSourceFolder(settings.SourceFolder, legacyValue);
            MigrateLegacyAuth(source, mapping, resolved, targetSourceStateDir, warnings);
            source.Settings.Remove(mapping.LegacyKey);
            dirty = true;
        }

        if (string.Equals(source.TypeId, "HardDiskDrive", StringComparison.OrdinalIgnoreCase)
            && source.Settings.TryGetValue("path", out var hddPath)
            && !string.IsNullOrWhiteSpace(hddPath))
        {
            var resolvedHdd = ResolveFromSourceFolder(settings.SourceFolder, hddPath);
            if (!string.Equals(hddPath, resolvedHdd, StringComparison.Ordinal))
            {
                source.Settings["path"] = resolvedHdd;
                dirty = true;
            }

            hardDiskPaths.Add(resolvedHdd);
            warnings.Add(new(
                WinFormsImportWarningKind.ExternalHardDiskPath,
                source.Id,
                source.Title,
                $"Источник «{source.Title}» использует каталог HDD {resolvedHdd}. Не запускайте запись в общий каталог из WinForms и WPF одновременно."));
        }

        return dirty;
    }

    private static void MigrateLegacyAuth(
        Source source,
        LegacyAuthMapping mapping,
        string resolvedLegacyPath,
        string targetSourceStateDir,
        List<WinFormsImportWarning> warnings)
    {
        if (mapping.Kind == LegacyAuthKind.SingleFile)
        {
            Directory.CreateDirectory(targetSourceStateDir);
            var targetPath = Path.Combine(targetSourceStateDir, mapping.TargetName);

            if (!File.Exists(resolvedLegacyPath))
            {
                if (!File.Exists(targetPath))
                {
                    warnings.Add(new(
                        WinFormsImportWarningKind.MissingExternalSessionFile,
                        source.Id,
                        source.Title,
                        $"Файл сессии источника «{source.Title}» не найден: {resolvedLegacyPath}. Потребуется повторная авторизация."));
                }

                return;
            }

            if (IsYoutubeLegacyJsonFormat(source.TypeId, resolvedLegacyPath))
            {
                warnings.Add(new(
                    WinFormsImportWarningKind.LegacyYoutubePlaywrightAuth,
                    source.Id,
                    source.Title,
                    $"У источника «{source.Title}» пропущен старый Playwright JSON ({resolvedLegacyPath}) – авторизуйтесь в YouTube заново."));

                return;
            }

            if (!File.Exists(targetPath))
            {
                File.Copy(resolvedLegacyPath, targetPath);
            }

            return;
        }

        if (mapping.Kind == LegacyAuthKind.GoogleOAuthDir)
        {
            var legacyDir = Path.GetDirectoryName(resolvedLegacyPath);
            if (string.IsNullOrWhiteSpace(legacyDir) || !Directory.Exists(legacyDir))
            {
                warnings.Add(new(
                    WinFormsImportWarningKind.MissingExternalSessionFile,
                    source.Id,
                    source.Title,
                    $"Директория OAuth-токенов источника «{source.Title}» не найдена: {resolvedLegacyPath}."));
                return;
            }

            var targetOAuthDir = Path.Combine(targetSourceStateDir, mapping.TargetName);
            Directory.CreateDirectory(targetOAuthDir);

            var copiedOrExisting = 0;
            foreach (var file in Directory.EnumerateFiles(legacyDir, "Google.Apis.Auth.OAuth2.Responses.*"))
            {
                var destination = Path.Combine(targetOAuthDir, Path.GetFileName(file));
                if (!File.Exists(destination))
                {
                    File.Copy(file, destination);
                }

                copiedOrExisting++;
            }

            if (copiedOrExisting == 0)
            {
                warnings.Add(new(
                    WinFormsImportWarningKind.MissingExternalSessionFile,
                    source.Id,
                    source.Title,
                    $"В каталоге {legacyDir} не найдено файлов OAuth-токенов для «{source.Title}»."));
            }
        }
    }

    private static int CopyAndRebindCoverTemplates(
        string sourceFolder,
        string sourceCoversDir,
        string stagedCoversDir,
        string finalCoversDir,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceCoversDir))
        {
            return 0;
        }

        CopyDirectoryRecursive(sourceCoversDir, stagedCoversDir, overwrite: true, cancellationToken);

        var canonicalSourceCovers = WinFormsInstallationInspector.CanonicalizePath(sourceCoversDir);
        var jsonFiles = Directory.EnumerateFiles(stagedCoversDir, "*.json", SearchOption.TopDirectoryOnly).ToList();

        foreach (var jsonFile in jsonFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RebindCoverTemplateJson(jsonFile, sourceFolder, canonicalSourceCovers, finalCoversDir);
        }

        return jsonFiles.Count;
    }

    private static void RebindCoverTemplateJson(
        string jsonFilePath,
        string sourceFolder,
        string canonicalSourceCovers,
        string finalCoversDir)
    {
        try
        {
            var rawJson = File.ReadAllText(jsonFilePath);
            var rootNode = JsonNode.Parse(rawJson);
            if (rootNode is not JsonObject obj)
            {
                return;
            }

            if (!obj.TryGetPropertyValue("TemplatePath", out var pathNode) || pathNode is null)
            {
                return;
            }

            var rawTemplatePath = pathNode.GetValue<string>();
            if (string.IsNullOrWhiteSpace(rawTemplatePath))
            {
                return;
            }

            string resolvedPath;
            if (Path.IsPathRooted(rawTemplatePath))
            {
                resolvedPath = Path.GetFullPath(rawTemplatePath);
            }
            else
            {
                var inCovers = Path.GetFullPath(Path.Combine(canonicalSourceCovers, rawTemplatePath));
                resolvedPath = File.Exists(inCovers)
                    ? inCovers
                    : Path.GetFullPath(Path.Combine(sourceFolder, rawTemplatePath));
            }

            var canonicalResolved = WinFormsInstallationInspector.CanonicalizePath(resolvedPath);
            string reboundPath;

            if (WinFormsInstallationInspector.IsSameOrNestedPath(canonicalResolved, canonicalSourceCovers))
            {
                var relative = Path.GetRelativePath(canonicalSourceCovers, canonicalResolved);
                reboundPath = Path.Combine(finalCoversDir, relative);
            }
            else
            {
                reboundPath = resolvedPath;
            }

            if (!string.Equals(rawTemplatePath, reboundPath, StringComparison.Ordinal))
            {
                obj["TemplatePath"] = reboundPath;
                File.WriteAllText(jsonFilePath, obj.ToJsonString(JsonOptions));
            }
        }
        catch
        {
        }
    }

    private static string ResolveFromSourceFolder(string sourceFolder, string rawPath)
    {
        return LegacySettingsParser.ResolvePath(sourceFolder, rawPath, ".");
    }

    private static bool IsYoutubeLegacyJsonFormat(string typeId, string path)
    {
        if (!string.Equals(typeId, "Youtube", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var reader = new StreamReader(path);
            int ch;
            while ((ch = reader.Read()) != -1)
            {
                if (!char.IsWhiteSpace((char)ch))
                {
                    return (char)ch == '{';
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private sealed record LegacyAuthMapping(string TypeId, string LegacyKey, LegacyAuthKind Kind, string TargetName);
}
