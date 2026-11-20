using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Domain;

public sealed partial class SourceAuthenticationService
{
    public bool CanCopyAuthentication(Source source, Source target) =>
        GetCopyPlan(source, target).Length > 0;

    public async Task CopyAuthenticationAsync(Source source, Source target, CancellationToken cancellationToken)
    {
        if (source.Id == target.Id)
        {
            throw new InvalidOperationException("Выберите другой источник.");
        }

        var sourceLock = GetSourceLock(source.Id);
        var targetLock = GetSourceLock(target.Id);
        var (first, second) = StringComparer.Ordinal.Compare(source.Id, target.Id) < 0
            ? (sourceLock, targetLock) : (targetLock, sourceLock);
        await first.WaitAsync(cancellationToken);
        try
        {
            await second.WaitAsync(cancellationToken);
            try
            {
                await CopyAuthenticationCoreAsync(source, target, cancellationToken);
            }
            finally
            {
                second.Release();
            }
        }
        finally
        {
            first.Release();
        }
    }

    private async Task CopyAuthenticationCoreAsync(Source source, Source target, CancellationToken cancellationToken)
    {
        if (source.Type is not ISourceAuthenticationProbe sourceProbe
            || target.Type is not ISourceAuthenticationProbe targetProbe
            || GetCopyPlan(source, target).Length == 0)
        {
            throw new InvalidOperationException("У этих источников нет совместимых данных входа.");
        }

        var targetRoot = target.Settings["_system_state_path"];
        var staging = Directory.CreateDirectory(Path.Combine(targetRoot, ".auth-copy-" + Guid.NewGuid().ToString("N")));
        var committed = new Stack<(string Target, string? Backup)>();
        var keepBackup = false;
        try
        {
            (string From, string To)[] files = [];
            await sourceProbe.ReadAuthenticationFilesAsync(CopySettings(source), async () =>
            {
                files = GetCopyPlan(source, target);
                if (files.Length == 0)
                {
                    throw new InvalidOperationException("У этих источников нет совместимых данных входа.");
                }

                for (var i = 0; i < files.Length; i++)
                {
                    await using var input = new FileStream(files[i].From, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
                    await using var output = File.Create(Path.Combine(staging.FullName, $"new-{i}"), 4096, FileOptions.Asynchronous);
                    await input.CopyToAsync(output, cancellationToken);
                }
            }, cancellationToken);

            await targetProbe.ReplaceAuthenticationFilesAsync(CopySettings(target), () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    for (var i = 0; i < files.Length; i++)
                    {
                        var destination = files[i].To;
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        var staged = Path.Combine(staging.FullName, $"new-{i}");
                        if (File.Exists(destination))
                        {
                            var backup = Path.Combine(staging.FullName, $"old-{i}");
                            try
                            {
                                File.Replace(staged, destination, backup);
                            }
                            finally
                            {
                                if (File.Exists(backup))
                                {
                                    committed.Push((destination, backup));
                                }
                            }
                        }
                        else
                        {
                            File.Move(staged, destination);
                            committed.Push((destination, null));
                        }
                    }
                }
                catch
                {
                    while (committed.TryPop(out var file))
                    {
                        try
                        {
                            if (file.Backup is null)
                            {
                                File.Delete(file.Target);
                            }
                            else
                            {
                                File.Move(file.Backup, file.Target, overwrite: true);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            keepBackup = true;
                            logger.LogError("Не удалось восстановить авторизацию источника {TargetId}. Резервная папка: {Staging}. Ошибка {ErrorType}",
                                target.Id, staging.Name, ex.GetType().Name);
                        }
                    }

                    throw;
                }

                return Task.CompletedTask;
            }, cancellationToken);
        }
        finally
        {
            if (!keepBackup)
            {
                try
                {
                    staging.Delete(recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning("Не удалось удалить временную папку копирования для {TargetId}: {ErrorType}",
                        target.Id, ex.GetType().Name);
                }
            }
        }

        orcestrator.InvalidateAuthentication(target.Id);
    }

    private static (string From, string To)[] GetCopyPlan(Source source, Source target)
    {
        if (source.Id == target.Id || source.Type is not ISourceAuthenticationProbe probe
            || source.Type.GetType() != target.Type?.GetType()
            || !source.TypeId.Equals(target.TypeId, StringComparison.OrdinalIgnoreCase)
            || source.Settings.GetValueOrDefault("_system_state_path") is not { Length: > 0 } sourceRoot
            || target.Settings.GetValueOrDefault("_system_state_path") is not { Length: > 0 } targetRoot)
        {
            return [];
        }

        sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetRoot));
        if (IsUnder(sourceRoot, targetRoot) || IsUnder(targetRoot, sourceRoot))
        {
            return [];
        }

        return probe.GetCopyableAuthenticationFiles(CopySettings(source), CopySettings(target))
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => IsUnder(path, sourceRoot) && IsOrdinaryPath(sourceRoot, path))
            .Select(path => (path, Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, path))))
            .Where(file => IsOrdinaryPath(targetRoot, file.Item2))
            .ToArray();
    }

    private static bool IsUnder(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsOrdinaryPath(string root, string path)
    {
        for (var current = path; ; current = Path.GetDirectoryName(current)!)
        {
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                {
                    return false;
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }

            if (current.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
    }
}
