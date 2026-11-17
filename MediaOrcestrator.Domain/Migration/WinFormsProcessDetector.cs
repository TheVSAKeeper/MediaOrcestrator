using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace MediaOrcestrator.Domain.Migration;

public sealed class WinFormsProcessDetector : IWinFormsProcessDetector
{
    private const string ClassicExecutableName = "MediaOrcestrator.Runner";

    public static string ComputeMutexName(string sourceFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);

        var normalized = Path.GetFullPath(sourceFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(normalized)));
        return $@"Global\MediaOrcestrator.{hash}";
    }

    public static string GetWalLogPath(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var full = Path.GetFullPath(databasePath);
        var dir = Path.GetDirectoryName(full)!;
        var name = Path.GetFileNameWithoutExtension(full);
        var ext = Path.GetExtension(full);
        return Path.Combine(dir, $"{name}-log{ext}");
    }

    public WinFormsProcessStatus CheckRunning(string sourceFolder, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var canonicalSource = WinFormsInstallationInspector.CanonicalizePath(sourceFolder);
        var isMutexHeld = IsAnyMutexHeld(sourceFolder, canonicalSource);
        var runningPids = FindRunningProcessIds(canonicalSource);
        var isDbLocked = IsFileLocked(databasePath);
        var isWalLocked = IsFileLocked(GetWalLogPath(databasePath));
        var isBusy = isMutexHeld || runningPids.Count > 0 || isDbLocked || isWalLocked;

        return new(isBusy, isMutexHeld, runningPids, isDbLocked, isWalLocked);
    }

    public async Task WaitForCloseAsync(
        string sourceFolder,
        string databasePath,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default)
    {
        var effectiveInterval = pollInterval > TimeSpan.Zero
            ? pollInterval
            : TimeSpan.FromMilliseconds(500);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = CheckRunning(sourceFolder, databasePath);
            if (!status.IsBusy)
            {
                return;
            }

            await Task.Delay(effectiveInterval, cancellationToken);
        }
    }

    private static bool IsAnyMutexHeld(string rawSourceFolder, string canonicalSourceFolder)
    {
        var rawMutex = ComputeMutexName(rawSourceFolder);
        if (IsMutexHeld(rawMutex))
        {
            return true;
        }

        var canonicalMutex = ComputeMutexName(canonicalSourceFolder);
        return !string.Equals(rawMutex, canonicalMutex, StringComparison.OrdinalIgnoreCase)
               && IsMutexHeld(canonicalMutex);
    }

    private static bool IsMutexHeld(string mutexName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            if (Mutex.TryOpenExisting(mutexName, out var mutex))
            {
                mutex.Dispose();
                return true;
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    private static List<int> FindRunningProcessIds(string canonicalSourceFolder)
    {
        var result = new List<int>();
        Process[] processes;

        try
        {
            processes = Process.GetProcessesByName(ClassicExecutableName);
        }
        catch
        {
            return result;
        }

        foreach (var process in processes)
        {
            try
            {
                var modulePath = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(modulePath))
                {
                    continue;
                }

                var dir = Path.GetDirectoryName(modulePath);
                if (string.IsNullOrWhiteSpace(dir))
                {
                    continue;
                }

                var canonicalProcessDir = WinFormsInstallationInspector.CanonicalizePath(dir);
                if (string.Equals(canonicalProcessDir, canonicalSourceFolder, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(process.Id);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    private static bool IsFileLocked(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
