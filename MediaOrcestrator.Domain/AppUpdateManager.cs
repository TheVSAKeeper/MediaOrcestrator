using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;

namespace MediaOrcestrator.Domain;

public sealed class AppUpdateManager(
    IReleaseProvider releaseProvider,
    Func<string> updateRepo,
    string assetPrefix,
    ILogger<AppUpdateManager> logger)
{
    private readonly string _assetPattern = $"{assetPrefix}-v*.zip";

    private AppUpdateInfo? _cachedUpdate;
    private DateTimeOffset _lastChecked;
    private string? _cachedRepo;

    public Version CurrentVersion { get; } = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    public string? LastCheckError { get; private set; }

    public async Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var repo = updateRepo();

        if (string.Equals(_cachedRepo, repo, StringComparison.Ordinal)
            && DateTimeOffset.Now - _lastChecked < TimeSpan.FromHours(1)
            && _cachedUpdate is not null)
        {
            return _cachedUpdate;
        }

        try
        {
            var release = await releaseProvider.GetLatestReleaseAsync(repo, _assetPattern, cancellationToken);
            LastCheckError = null;

            if (release is null)
            {
                logger.LogWarning("Не удалось получить сведения о релизах для {Repo}", repo);
                LastCheckError = "GitHub не ответил или релизов ещё нет";
                _cachedUpdate = null;
                return null;
            }

            if (release.AssetUrl is null)
            {
                logger.LogDebug("Нет доступных релизов для {Repo}", repo);
                _cachedUpdate = null;
                return null;
            }

            var tagVersion = release.TagName.StartsWith('v') ? release.TagName[1..] : release.TagName;

            if (!Version.TryParse(tagVersion, out var latestVersion) || latestVersion <= CurrentVersion)
            {
                logger.LogDebug("Текущая версия {Current} актуальна (последняя: {Latest})",
                    CurrentVersion, tagVersion);

                _cachedUpdate = null;
                return null;
            }

            _cachedUpdate = new(tagVersion,
                release.AssetUrl,
                release.Body ?? "",
                release.AssetSize,
                release.AssetDigest);

            _lastChecked = DateTimeOffset.Now;
            _cachedRepo = repo;

            logger.LogInformation("Доступно обновление: {Version}", tagVersion);
            return _cachedUpdate;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Не удалось проверить обновления приложения");
            LastCheckError = "Проверка сорвалась, подробности в логе";
            _cachedRepo = null;
            return null;
        }
    }

    public async Task<string> DownloadUpdateAsync(
        AppUpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var backupsDir = Path.Combine(AppContext.BaseDirectory, "backups");
        var zipName = $"{assetPrefix}-v{update.Version}.zip";
        var backupZipPath = Path.Combine(backupsDir, zipName);

        if (File.Exists(backupZipPath) && new FileInfo(backupZipPath).Length == update.Size && IsIntact(backupZipPath, update.Digest))
        {
            logger.LogInformation("Обновление {Version} уже скачано: {Path}", update.Version, backupZipPath);
            return backupZipPath;
        }

        Directory.CreateDirectory(backupsDir);

        var tempPath = backupZipPath + ".downloading";

        logger.LogInformation("Скачивание обновления {Version}...", update.Version);
        await releaseProvider.DownloadAssetAsync(update.DownloadUrl, tempPath, update.Digest, progress, cancellationToken);

        File.Move(tempPath, backupZipPath, true);
        logger.LogInformation("Обновление скачано: {Path}", backupZipPath);

        return backupZipPath;
    }

    private bool IsIntact(string zipPath, string? digest)
    {
        try
        {
            FileIntegrity.EnsureMatches(zipPath, digest);
            return true;
        }
        catch (InvalidDataException exception)
        {
            logger.LogWarning(exception, "Ранее скачанный архив обновления повреждён, будет скачан заново");
            return false;
        }
    }

    public void ApplyUpdate(string zipPath)
    {
        var appDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var updaterPath = Path.Combine(appDir, "Updater.exe");

        if (!File.Exists(updaterPath))
        {
            throw new FileNotFoundException("Updater.exe не найден в папке приложения", updaterPath);
        }

        var pid = Environment.ProcessId;
        var currentVersion = CurrentVersion.ToString();
        var restart = Path.GetFileName(Environment.ProcessPath)
                      ?? throw new InvalidOperationException("Не удалось определить exe приложения для перезапуска");

        var args = $"--zip \"{zipPath}\" --target \"{appDir}\" --pid {pid} --restart \"{restart}\" --current-version {currentVersion}";

        logger.LogInformation("Запуск Updater: {Args}", args);

        Process.Start(new ProcessStartInfo
        {
            FileName = updaterPath,
            Arguments = args,
            UseShellExecute = true,
        });

        Environment.Exit(0);
    }
}
