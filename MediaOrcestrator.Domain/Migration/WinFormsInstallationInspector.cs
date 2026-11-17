using LiteDB;
using System.Diagnostics;
using System.Reflection;
using System.Security;

namespace MediaOrcestrator.Domain.Migration;

public sealed class WinFormsInstallationInspector(
    IWinFormsProcessDetector processDetector,
    Func<string, Version?>? versionReader = null)
{
    public static readonly Version MinimumSupportedVersion = new(0, 0, 5, 9);

    public WinFormsInstallationInspector(IWinFormsProcessDetector processDetector)
        : this(processDetector, null)
    {
    }

    public static string CanonicalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path.Trim());
        var root = Path.GetPathRoot(full);

        if (string.IsNullOrEmpty(root))
        {
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        var relative = full[root.Length..].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(relative))
        {
            return ResolveDirectoryTarget(root);
        }

        var current = ResolveDirectoryTarget(root);
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
            var isLast = i == segments.Length - 1;

            if (Directory.Exists(current))
            {
                current = ResolveDirectoryTarget(current);
            }
            else if (isLast && File.Exists(current))
            {
                current = ResolveFileTarget(current);
            }
        }

        var normalized = Path.GetFullPath(current);
        var normalizedRoot = Path.GetPathRoot(normalized);

        if (!string.IsNullOrEmpty(normalizedRoot) && string.Equals(normalized, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool IsSameOrNestedPath(string candidateCanonical, string baseCanonical)
    {
        if (string.Equals(candidateCanonical, baseCanonical, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = baseCanonical.EndsWith(Path.DirectorySeparatorChar)
            ? baseCanonical
            : baseCanonical + Path.DirectorySeparatorChar;

        return candidateCanonical.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public WinFormsInspectionResult Inspect(string sourceFolder, string? currentAppBaseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            return Failure(sourceFolder ?? string.Empty, string.Empty, WinFormsInspectionIssueCode.FolderNotFound, "Укажите папку с установленной версией WinForms.");
        }

        string canonicalSource;
        try
        {
            canonicalSource = CanonicalizePath(sourceFolder);
        }
        catch (Exception ex)
        {
            return Failure(sourceFolder, string.Empty, WinFormsInspectionIssueCode.FolderNotFound, $"Некорректный путь к папке WinForms: {ex.Message}");
        }

        if (!Directory.Exists(canonicalSource))
        {
            return Failure(sourceFolder, canonicalSource, WinFormsInspectionIssueCode.FolderNotFound, "Указанная папка не существует.");
        }

        var canonicalCurrentApp = CanonicalizePath(string.IsNullOrWhiteSpace(currentAppBaseDirectory) ? AppContext.BaseDirectory : currentAppBaseDirectory);
        if (string.Equals(canonicalSource, canonicalCurrentApp, StringComparison.OrdinalIgnoreCase))
        {
            return Failure(sourceFolder, canonicalSource, WinFormsInspectionIssueCode.SelfTarget, "Нельзя импортировать данные из текущей папки WPF.");
        }

        var wpfDll = Path.Combine(canonicalSource, "MediaOrcestrator.Runner.Wpf.dll");
        var wpfExe = Path.Combine(canonicalSource, "MediaOrcestrator.Runner.Wpf.exe");
        var classicDll = Path.Combine(canonicalSource, "MediaOrcestrator.Runner.dll");
        var classicExe = Path.Combine(canonicalSource, "MediaOrcestrator.Runner.exe");

        if ((File.Exists(wpfDll) || File.Exists(wpfExe)) && !File.Exists(classicDll))
        {
            return Failure(sourceFolder, canonicalSource, WinFormsInspectionIssueCode.WpfInstallation, "Выбрана папка WPF-приложения, а не классического WinForms.");
        }

        if (!File.Exists(classicDll) && !File.Exists(classicExe))
        {
            return Failure(sourceFolder, canonicalSource, WinFormsInspectionIssueCode.MissingWinFormsExecutable, "В папке не найден исполняемый файл WinForms (MediaOrcestrator.Runner.exe / .dll).");
        }

        var settingsTxtPath = Path.Combine(canonicalSource, "settings.txt");
        if (!File.Exists(settingsTxtPath))
        {
            return Failure(sourceFolder, canonicalSource, WinFormsInspectionIssueCode.MissingSettingsFile, "В выбранной папке не найден файл настроек settings.txt.");
        }

        var versionTarget = File.Exists(classicDll) ? classicDll : classicExe;
        var detectedVersion = ReadVersion(versionTarget);

        if (detectedVersion is null || detectedVersion < MinimumSupportedVersion)
        {
            var versionMessage = detectedVersion is null
                ? $"Не удалось определить версию WinForms. Требуется версия {MinimumSupportedVersion} или выше."
                : $"Обнаружена версия WinForms {detectedVersion}. Перед переносом обновите её до {MinimumSupportedVersion}.";

            return new(
                false,
                sourceFolder,
                canonicalSource,
                WinFormsInspectionIssueCode.UnsupportedVersion,
                versionMessage,
                detectedVersion,
                null,
                WinFormsProcessStatus.Free,
                null);
        }

        LegacySettingsSnapshot settings;
        try
        {
            var rawValues = LegacySettingsParser.ParseFile(settingsTxtPath);
            settings = LegacySettingsParser.ResolveSnapshot(canonicalSource, rawValues);
        }
        catch (Exception ex)
        {
            return Failure(sourceFolder, canonicalSource, WinFormsInspectionIssueCode.InspectionFailed, $"Не удалось прочитать settings.txt: {ex.Message}", detectedVersion);
        }

        if (!File.Exists(settings.DatabasePath))
        {
            return Failure(sourceFolder, canonicalSource, WinFormsInspectionIssueCode.MissingDatabase, $"Не найден файл базы данных: {settings.DatabasePath}", detectedVersion, settings);
        }

        var dbDir = Path.GetDirectoryName(settings.DatabasePath)!;
        var dbName = Path.GetFileNameWithoutExtension(settings.DatabasePath);
        var restorePendingPath = Path.Combine(dbDir, $"{dbName}.restore-pending");
        if (File.Exists(restorePendingPath))
        {
            return Failure(
                sourceFolder,
                canonicalSource,
                WinFormsInspectionIssueCode.PendingDatabaseRestore,
                "В WinForms ожидает применение резервной копии (.restore-pending). Запустите и закройте WinForms, чтобы восстановление завершилось.",
                detectedVersion,
                settings);
        }

        var processStatus = processDetector.CheckRunning(canonicalSource, settings.DatabasePath);
        if (processStatus.IsBusy)
        {
            return new(
                true,
                sourceFolder,
                canonicalSource,
                WinFormsInspectionIssueCode.None,
                null,
                detectedVersion,
                settings,
                processStatus,
                null);
        }

        try
        {
            var summary = ReadDatabaseSummarySafely(settings);
            return new(
                true,
                sourceFolder,
                canonicalSource,
                WinFormsInspectionIssueCode.None,
                null,
                detectedVersion,
                settings,
                processStatus,
                summary);
        }
        catch (Exception ex)
        {
            return Failure(
                sourceFolder,
                canonicalSource,
                WinFormsInspectionIssueCode.InspectionFailed,
                $"Не удалось прочитать сводку базы данных WinForms: {ex.Message}",
                detectedVersion,
                settings);
        }
    }

    private Version? ReadVersion(string filePath)
    {
        if (versionReader is not null)
        {
            return versionReader(filePath);
        }

        return ReadAssemblyOrFileVersion(filePath);
    }

    internal static Version? ReadAssemblyOrFileVersion(string filePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(filePath);
            if (TryParseCleanVersion(info.FileVersion, out var fileVersion))
            {
                return fileVersion;
            }

            if (TryParseCleanVersion(info.ProductVersion, out var productVersion))
            {
                return productVersion;
            }
        }
        catch
        {
        }

        try
        {
            return AssemblyName.GetAssemblyName(filePath).Version;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseCleanVersion(string? raw, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        var plusIndex = trimmed.IndexOf('+');
        if (plusIndex >= 0)
        {
            trimmed = trimmed[..plusIndex];
        }

        var dashIndex = trimmed.IndexOf('-');
        if (dashIndex >= 0)
        {
            trimmed = trimmed[..dashIndex];
        }

        return Version.TryParse(trimmed, out version);
    }

    private WinFormsDatabaseSummary ReadDatabaseSummarySafely(LegacySettingsSnapshot settings)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mo-import-inspect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var walPath = WinFormsProcessDetector.GetWalLogPath(settings.DatabasePath);
            var hasWal = File.Exists(walPath);

            var dbSize = new FileInfo(settings.DatabasePath).Length;
            var walSize = hasWal ? new FileInfo(walPath).Length : 0L;

            var shaBefore = FileIntegrity.ComputeSha256(settings.DatabasePath);
            var walShaBefore = hasWal ? FileIntegrity.ComputeSha256(walPath) : null;

            var tempDbPath = Path.Combine(tempDir, "inspect.db");
            var tempWalPath = WinFormsProcessDetector.GetWalLogPath(tempDbPath);

            File.Copy(settings.DatabasePath, tempDbPath, overwrite: true);
            if (hasWal)
            {
                File.Copy(walPath, tempWalPath, overwrite: true);
            }

            var hasWalAfter = File.Exists(walPath);
            var shaAfter = FileIntegrity.ComputeSha256(settings.DatabasePath);
            var walShaAfter = hasWalAfter ? FileIntegrity.ComputeSha256(walPath) : null;

            if (hasWal != hasWalAfter
                || !string.Equals(shaBefore, shaAfter, StringComparison.Ordinal)
                || !string.Equals(walShaBefore, walShaAfter, StringComparison.Ordinal)
                || processDetector.CheckRunning(settings.SourceFolder, settings.DatabasePath).IsBusy)
            {
                throw new IOException("База данных изменилась во время чтения предпросмотра.");
            }

            using var db = new LiteDatabase(tempDbPath);
            var sources = db.GetCollection<Source>("sources").FindAll().ToList();
            var mediasCol = db.GetCollection("medias");
            var mediaDocs = mediasCol.FindAll().ToList();
            var mediasCount = mediaDocs.Count;
            var linksCount = mediaDocs.Sum(doc => doc.TryGetValue("Sources", out var arr) && arr.IsArray ? arr.AsArray.Count : 0);
            var relationsCount = db.GetCollection("source_relations").Count();

            var coversDir = Path.Combine(settings.SourceFolder, "templates", "covers");
            var coversCount = Directory.Exists(coversDir)
                ? Directory.EnumerateFiles(coversDir, "*.json", SearchOption.TopDirectoryOnly).Count()
                : 0;

            var warnings = new List<WinFormsImportWarning>();
            var hardDiskPaths = new List<string>();

            if (hasWal)
            {
                warnings.Add(new(
                    WinFormsImportWarningKind.PendingWalLog,
                    string.Empty,
                    string.Empty,
                    $"Обнаружен журнал {Path.GetFileName(walPath)} – при переносе несохранённые в основной файл записи будут проиграны в копию."));
            }

            foreach (var source in sources)
            {
                WinFormsSnapshotStager.CollectSourceInspectionWarnings(source, settings, hardDiskPaths, warnings);
            }

            return new(
                sources.Count,
                mediasCount,
                linksCount,
                relationsCount,
                coversCount,
                hasWal,
                dbSize,
                walSize,
                hardDiskPaths,
                warnings);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string ResolveDirectoryTarget(string dirPath)
    {
        try
        {
            var target = new DirectoryInfo(dirPath).ResolveLinkTarget(returnFinalTarget: true);
            return target?.FullName ?? dirPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return dirPath;
        }
    }

    private static string ResolveFileTarget(string filePath)
    {
        try
        {
            var target = new FileInfo(filePath).ResolveLinkTarget(returnFinalTarget: true);
            return target?.FullName ?? filePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return filePath;
        }
    }

    private static WinFormsInspectionResult Failure(
        string requestedPath,
        string canonicalPath,
        WinFormsInspectionIssueCode code,
        string message,
        Version? detectedVersion = null,
        LegacySettingsSnapshot? settings = null)
    {
        return new(
            false,
            requestedPath,
            canonicalPath,
            code,
            message,
            detectedVersion,
            settings,
            WinFormsProcessStatus.Free,
            null);
    }

    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }
}
