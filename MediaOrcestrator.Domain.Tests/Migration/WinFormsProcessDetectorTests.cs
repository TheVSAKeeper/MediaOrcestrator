using MediaOrcestrator.Domain.Migration;

namespace MediaOrcestrator.Domain.Tests.Migration;

[TestFixture]
public sealed class WinFormsProcessDetectorTests
{
    private string _rootDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "mo-detector-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_rootDir))
        {
            Directory.Delete(_rootDir, true);
        }
    }

    [Test]
    public void CheckRunning_занятый_мьютекс_определяет_экземпляр_как_занятый()
    {
        var sourceDir = Path.Combine(_rootDir, "wf-mutex");
        Directory.CreateDirectory(sourceDir);
        var dbPath = Path.Combine(sourceDir, "MyData.db");
        File.WriteAllBytes(dbPath, [1, 2, 3]);

        var mutexName = WinFormsProcessDetector.ComputeMutexName(sourceDir);
        var detector = new WinFormsProcessDetector();

        using var mutex = new Mutex(true, mutexName, out var createdNew);
        var busyStatus = detector.CheckRunning(sourceDir, dbPath);

        mutex.ReleaseMutex();
        mutex.Dispose();
        var freeStatus = detector.CheckRunning(sourceDir, dbPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(createdNew, Is.True);
            Assert.That(busyStatus.IsBusy, Is.True);
            Assert.That(busyStatus.IsMutexHeld, Is.True);
            Assert.That(freeStatus.IsBusy, Is.False);
            Assert.That(freeStatus.IsMutexHeld, Is.False);
        }
    }

    [Test]
    public void CheckRunning_удерживаемый_дескриптор_бд_и_журнала_определяет_блокировку()
    {
        var sourceDir = Path.Combine(_rootDir, "wf-lock");
        Directory.CreateDirectory(sourceDir);
        var dbPath = Path.Combine(sourceDir, "catalog.db");
        var walPath = WinFormsProcessDetector.GetWalLogPath(dbPath);
        File.WriteAllBytes(dbPath, [1, 2, 3]);
        File.WriteAllBytes(walPath, [4, 5, 6]);

        var detector = new WinFormsProcessDetector();

        WinFormsProcessStatus dbLockedStatus;
        using (var dbLock = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            dbLockedStatus = detector.CheckRunning(sourceDir, dbPath);
        }

        WinFormsProcessStatus walLockedStatus;
        using (var walLock = new FileStream(walPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            walLockedStatus = detector.CheckRunning(sourceDir, dbPath);
        }

        var freeStatus = detector.CheckRunning(sourceDir, dbPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dbLockedStatus.IsBusy, Is.True);
            Assert.That(dbLockedStatus.IsDatabaseLocked, Is.True);
            Assert.That(walLockedStatus.IsBusy, Is.True);
            Assert.That(walLockedStatus.IsWalLocked, Is.True);
            Assert.That(freeStatus.IsBusy, Is.False);
        }
    }

    [Test]
    public async Task WaitForCloseAsync_продолжает_выполнение_после_освобождения_блокировки()
    {
        var sourceDir = Path.Combine(_rootDir, "wf-wait");
        Directory.CreateDirectory(sourceDir);
        var dbPath = Path.Combine(sourceDir, "MyData.db");
        File.WriteAllBytes(dbPath, [1, 2, 3]);

        var detector = new WinFormsProcessDetector();
        var lockStream = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var releaseTask = Task.Run(async () =>
        {
            await Task.Delay(120);
            lockStream.Dispose();
        });

        await detector.WaitForCloseAsync(sourceDir, dbPath, TimeSpan.FromMilliseconds(30));
        await releaseTask;

        Assert.That(detector.CheckRunning(sourceDir, dbPath).IsBusy, Is.False);
    }

    [Test]
    public async Task WaitForCloseAsync_отменяется_по_CancellationToken()
    {
        var sourceDir = Path.Combine(_rootDir, "wf-cancel");
        Directory.CreateDirectory(sourceDir);
        var dbPath = Path.Combine(sourceDir, "MyData.db");
        File.WriteAllBytes(dbPath, [1, 2, 3]);

        var detector = new WinFormsProcessDetector();
        using var lockStream = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var cts = new CancellationTokenSource(100);

        var cancelled = await CaptureCancellationAsync(
            () => detector.WaitForCloseAsync(sourceDir, dbPath, TimeSpan.FromMilliseconds(30), cts.Token));

        Assert.That(cancelled, Is.True);
    }

    private static async Task<bool> CaptureCancellationAsync(Func<Task> action)
    {
        try
        {
            await action();
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
