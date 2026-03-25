using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MediaOrcestrator.Domain;

public class ToolManager(string toolsRoot, GitHubReleaseProvider releaseProvider, ILogger<ToolManager> logger)
{
    private readonly Dictionary<string, ToolDescriptor> _registry = new();
    private readonly Dictionary<string, List<IToolConsumer>> _consumers = new();
    private readonly Dictionary<string, string?> _resolvedPaths = new();
    private readonly Dictionary<string, DateTimeOffset> _lastChecked = new();
    private readonly Dictionary<string, ToolStatus> _cachedStatuses = new();

    public void RegisterTools(IEnumerable<IToolConsumer> consumers)
    {
        foreach (var consumer in consumers)
        {
            foreach (var tool in consumer.RequiredTools)
            {
                ValidateDescriptor(tool);

                if (_registry.TryGetValue(tool.Name, out var existing))
                {
                    if (existing.GitHubRepo != tool.GitHubRepo)
                    {
                        throw new InvalidOperationException($"Конфликт регистрации инструмента '{tool.Name}': '{existing.GitHubRepo}' vs '{tool.GitHubRepo}'");
                    }
                }
                else
                {
                    _registry[tool.Name] = tool;
                    _consumers[tool.Name] = [];
                }

                _consumers[tool.Name].Add(consumer);
            }
        }

        logger.LogInformation("Зарегистрировано {Count} инструментов: {Names}", _registry.Count, string.Join(", ", _registry.Keys));
    }

    public void ResolveAll()
    {
        Directory.CreateDirectory(toolsRoot);

        foreach (var (name, descriptor) in _registry)
        {
            var toolDir = Path.Combine(toolsRoot, name);
            var resolvedPath = FindExecutableInToolDir(toolDir, descriptor)
                               ?? TryMigrateFromLegacy(name, descriptor);

            _resolvedPaths[name] = resolvedPath;

            foreach (var consumer in _consumers[name])
            {
                consumer.SetToolPath(name, resolvedPath);
            }

            if (resolvedPath is not null)
            {
                logger.LogInformation("Инструмент '{Name}' найден: {Path}", name, resolvedPath);
            }
            else
            {
                logger.LogWarning("Инструмент '{Name}' не найден. Установите через панель инструментов", name);
            }
        }
    }

    public async Task<List<ToolStatus>> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ToolStatus>();

        foreach (var (name, descriptor) in _registry)
        {
            var installedVersion = GetInstalledVersion(name, descriptor);

            if (_lastChecked.TryGetValue(name, out var lastCheck)
                && DateTimeOffset.Now - lastCheck < TimeSpan.FromHours(1)
                && _cachedStatuses.TryGetValue(name, out var cached))
            {
                results.Add(cached with { InstalledVersion = installedVersion });
                continue;
            }

            var release = await releaseProvider.GetLatestReleaseAsync(descriptor.GitHubRepo, descriptor.AssetPattern, cancellationToken);

            var latestVersion = release is not null
                ? NormalizeTagVersion(release.TagName, descriptor.VersionTagPattern)
                : null;

            var updateAvailable = installedVersion is not null
                                  && latestVersion is not null
                                  && NormalizeForComparison(installedVersion) != NormalizeForComparison(latestVersion)
                                  || installedVersion is null
                                  && latestVersion is not null;

            _lastChecked[name] = DateTimeOffset.Now;

            var status = new ToolStatus
            {
                Name = name,
                InstalledVersion = installedVersion,
                LatestVersion = latestVersion,
                UpdateAvailable = updateAvailable,
                ResolvedPath = _resolvedPaths.GetValueOrDefault(name),
                LastChecked = _lastChecked[name],
            };

            _cachedStatuses[name] = status;
            results.Add(status);
        }

        return results;
    }

    public async Task UpdateToolAsync(string toolName, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGetValue(toolName, out var descriptor))
        {
            throw new ArgumentException($"Неизвестный инструмент: {toolName}");
        }

        var toolDir = Path.Combine(toolsRoot, toolName);
        var backupDir = toolDir + ".old";

        var currentPath = _resolvedPaths.GetValueOrDefault(toolName);

        if (currentPath is not null && File.Exists(currentPath))
        {
            var exeName = Path.GetFileNameWithoutExtension(currentPath);
            var processes = Process.GetProcessesByName(exeName);

            foreach (var process in processes)
            {
                try
                {
                    if (process.MainModule?.FileName?.StartsWith(toolDir, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        throw new InvalidOperationException($"Инструмент '{toolName}' сейчас используется (PID {process.Id}). Дождитесь завершения операции перед обновлением.");
                    }
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        var release = await releaseProvider.GetLatestReleaseAsync(descriptor.GitHubRepo, descriptor.AssetPattern, cancellationToken);

        if (release?.AssetUrl is null)
        {
            throw new InvalidOperationException($"Не найден файл для скачивания инструмента '{toolName}'");
        }

        var tempDir = Path.Combine(toolsRoot, ".temp", toolName);

        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }

            Directory.CreateDirectory(tempDir);

            var downloadPath = Path.Combine(tempDir, release.AssetName ?? $"{toolName}.download");

            logger.LogInformation("Скачивание {Tool} из {Url}...", toolName, release.AssetUrl);

            await releaseProvider.DownloadAssetAsync(release.AssetUrl, downloadPath, progress, cancellationToken);

            if (descriptor.ArchiveType != ArchiveType.None)
            {
                var extractDir = Path.Combine(tempDir, "extracted");
                ArchiveExtractor.Extract(downloadPath, extractDir, descriptor.ArchiveType);
                File.Delete(downloadPath);

                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, true);
                }

                if (Directory.Exists(toolDir))
                {
                    Directory.Move(toolDir, backupDir);
                }

                if (descriptor.ArchiveExecutablePath is not null)
                {
                    var exePath = ArchiveExtractor.FindExecutable(extractDir, descriptor.ArchiveExecutablePath);

                    if (exePath is null)
                    {
                        throw new FileNotFoundException($"Исполняемый файл не найден в архиве по паттерну '{descriptor.ArchiveExecutablePath}'");
                    }

                    var exeDir = Path.GetDirectoryName(exePath)!;
                    Directory.Move(exeDir, toolDir);
                }
                else
                {
                    Directory.Move(extractDir, toolDir);
                }
            }
            else
            {
                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, true);
                }

                if (Directory.Exists(toolDir))
                {
                    Directory.Move(toolDir, backupDir);
                }

                Directory.CreateDirectory(toolDir);
                var destFile = Path.Combine(toolDir, Path.GetFileName(downloadPath));
                File.Move(downloadPath, destFile);
            }

            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, true);
            }

            var resolvedPath = FindExecutableInToolDir(toolDir, descriptor);
            _resolvedPaths[toolName] = resolvedPath;

            foreach (var consumer in _consumers[toolName])
            {
                consumer.SetToolPath(toolName, resolvedPath);
            }

            logger.LogInformation("Инструмент '{Name}' успешно обновлён. Путь: {Path}", toolName, resolvedPath);
        }
        catch
        {
            if (Directory.Exists(backupDir) && !Directory.Exists(toolDir))
            {
                Directory.Move(backupDir, toolDir);
                logger.LogWarning("Откат '{Name}' к предыдущей версии", toolName);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                }
            }
        }
    }

    public string? GetToolPath(string toolName)
    {
        return _resolvedPaths.GetValueOrDefault(toolName);
    }

    public IReadOnlyDictionary<string, ToolDescriptor> GetRegistry()
    {
        return _registry;
    }

    private string? FindExecutableInToolDir(string toolDir, ToolDescriptor descriptor)
    {
        if (!Directory.Exists(toolDir))
        {
            return null;
        }

        if (descriptor.ArchiveExecutablePath is not null)
        {
            return ArchiveExtractor.FindExecutable(toolDir, descriptor.ArchiveExecutablePath)
                   ?? ArchiveExtractor.FindExecutable(toolDir, Path.GetFileName(descriptor.ArchiveExecutablePath));
        }

        var exeName = descriptor.AssetPattern.Contains('*')
            ? null
            : descriptor.AssetPattern;

        if (exeName is null)
        {
            var exeFiles = Directory.GetFiles(toolDir, "*.exe");
            return exeFiles.Length == 1 ? exeFiles[0] : null;
        }

        var path = Path.Combine(toolDir, exeName);
        return File.Exists(path) ? path : null;
    }

    private string? TryMigrateFromLegacy(string toolName, ToolDescriptor descriptor)
    {
        foreach (var consumer in _consumers[toolName])
        {
            var legacyPath = consumer.GetLegacyToolPath(toolName);

            if (legacyPath is null || !File.Exists(legacyPath))
            {
                continue;
            }

            logger.LogInformation("Миграция '{Name}' из старого пути: {Path}", toolName, legacyPath);

            var toolDir = Path.Combine(toolsRoot, toolName);
            Directory.CreateDirectory(toolDir);

            var destFile = Path.Combine(toolDir, Path.GetFileName(legacyPath));
            File.Copy(legacyPath, destFile, true);

            return FindExecutableInToolDir(toolDir, descriptor) ?? destFile;
        }

        return null;
    }

    private string? GetInstalledVersion(string toolName, ToolDescriptor descriptor)
    {
        var path = _resolvedPaths.GetValueOrDefault(toolName);

        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = descriptor.VersionCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);

            if (process is null)
            {
                return "unknown";
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit(10_000);

            var output = outputTask.GetAwaiter().GetResult();
            var errorOutput = errorTask.GetAwaiter().GetResult();

            var fullOutput = string.IsNullOrWhiteSpace(output) ? errorOutput : output;
            fullOutput = fullOutput.Trim();

            if (descriptor.VersionPattern is null)
            {
                return fullOutput.Split('\n')[0].Trim();
            }

            var match = Regex.Match(fullOutput, descriptor.VersionPattern);
            return match.Success ? match.Groups[1].Value : "unknown";

        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось получить версию '{Name}'", toolName);
            return "unknown";
        }
    }

    private static string NormalizeForComparison(string version)
    {
        return version.Replace("-", "").Replace(".", "");
    }

    private static string NormalizeTagVersion(string tag, string? versionTagPattern)
    {
        if (versionTagPattern is null)
        {
            return tag.StartsWith('v') ? tag[1..] : tag;
        }

        var match = Regex.Match(tag, versionTagPattern);
        return match.Success ? match.Groups[1].Value : tag;

    }

    private static void ValidateDescriptor(ToolDescriptor tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Name))
        {
            throw new ArgumentException("ToolDescriptor.Name обязателен");
        }

        if (string.IsNullOrWhiteSpace(tool.GitHubRepo))
        {
            throw new ArgumentException($"ToolDescriptor.GitHubRepo обязателен для '{tool.Name}'");
        }

        if (string.IsNullOrWhiteSpace(tool.AssetPattern))
        {
            throw new ArgumentException($"ToolDescriptor.AssetPattern обязателен для '{tool.Name}'");
        }

        if (string.IsNullOrWhiteSpace(tool.VersionCommand))
        {
            throw new ArgumentException($"ToolDescriptor.VersionCommand обязателен для '{tool.Name}'");
        }
    }
}
