using KeepShell.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MediaOrcestrator.Runner.Wpf.Services.Diagnostics;

public sealed class ChildProcessSampler(
    PerformanceMonitor monitor,
    ILogger<ChildProcessSampler> logger) : IHostedService, IDisposable
{
    private const uint Th32csSnapProcess = 0x00000002;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stopCts = new();
    private Task? _loopTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _loopTask = Task.Run(() => RunLoopAsync(_stopCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopCts.CancelAsync();

        if (_loopTask is null)
        {
            return;
        }

        try
        {
            await _loopTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _stopCts.Cancel();
        _stopCts.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);

        SampleOnce();

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                SampleOnce();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SampleOnce()
    {
        try
        {
            var usage = CollectDescendantUsage();
            monitor.ReportChildProcesses(usage);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Не удалось снять замер дочерних процессов");
        }
    }

    private static ChildProcessUsage CollectDescendantUsage()
    {
        var currentPid = Environment.ProcessId;
        var childrenByParent = SnapshotProcessTree();
        var queue = new Queue<int>();
        var visited = new HashSet<int> { currentPid };
        var totals = new Dictionary<string, (int Count, long PrivateBytes)>(StringComparer.OrdinalIgnoreCase);

        if (childrenByParent.TryGetValue(currentPid, out var direct))
        {
            foreach (var child in direct)
            {
                if (visited.Add(child.Pid))
                {
                    queue.Enqueue(child.Pid);
                }
            }
        }

        var exeByPid = new Dictionary<int, string>();

        foreach (var entry in childrenByParent.Values.SelectMany(list => list))
        {
            exeByPid.TryAdd(entry.Pid, entry.ExeName);
        }

        var totalCount = 0;
        long totalBytes = 0;

        while (queue.Count > 0)
        {
            var pid = queue.Dequeue();

            if (childrenByParent.TryGetValue(pid, out var nested))
            {
                foreach (var child in nested)
                {
                    if (visited.Add(child.Pid))
                    {
                        queue.Enqueue(child.Pid);
                    }
                }
            }

            if (!exeByPid.TryGetValue(pid, out var exeName))
            {
                continue;
            }

            long privateBytes = 0;

            try
            {
                using var process = Process.GetProcessById(pid);
                privateBytes = process.PrivateMemorySize64;
            }
            catch
            {
                continue;
            }

            totalCount++;
            totalBytes += privateBytes;

            var name = Path.GetFileNameWithoutExtension(exeName);
            totals.TryGetValue(name, out var existing);
            totals[name] = (existing.Count + 1, existing.PrivateBytes + privateBytes);
        }

        if (totalCount == 0)
        {
            return ChildProcessUsage.Empty;
        }

        var items = totals
            .OrderByDescending(pair => pair.Value.PrivateBytes)
            .Select(pair => new ChildProcessEntry(pair.Key, pair.Value.Count, pair.Value.PrivateBytes))
            .ToArray();

        return new ChildProcessUsage(DateTime.UtcNow, totalCount, totalBytes, items);
    }

    private static Dictionary<int, List<(int Pid, string ExeName)>> SnapshotProcessTree()
    {
        var map = new Dictionary<int, List<(int Pid, string ExeName)>>();
        var handle = NativeMethods.CreateToolhelp32Snapshot(Th32csSnapProcess, 0);

        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return map;
        }

        try
        {
            var entry = new PROCESSENTRY32W
            {
                dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>(),
            };

            if (!NativeMethods.Process32FirstW(handle, ref entry))
            {
                return map;
            }

            do
            {
                var parentPid = (int)entry.th32ParentProcessID;
                var pid = (int)entry.th32ProcessID;

                if (!map.TryGetValue(parentPid, out var list))
                {
                    list = [];
                    map[parentPid] = list;
                }

                list.Add((pid, entry.szExeFile ?? "process"));
            }
            while (NativeMethods.Process32NextW(handle, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }

        return map;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
