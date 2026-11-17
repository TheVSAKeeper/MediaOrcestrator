using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class AutoSyncHostedServiceTests
{
    private TestDomainContext _ctx = null!;
    private AutoSyncProbeSourceType _sourceType = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceType = new();
        _ctx = new([_sourceType]);
        _ctx.Database.GetCollection<Source>("sources").Insert(new Source
        {
            Id = "s1",
            TypeId = _sourceType.Name,
            Settings = new(),
        });
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task RunAsync_КогдаAutoSyncОтключён_НеВызываетСинхронизациюИВключаетсяНаСледующемТике()
    {
        var preferences = new ShellPreferences(_ctx.Settings)
        {
            AutoSyncEnabled = false,
            AutoSyncStartupDelaySeconds = 0,
        };
        var signal = new PreloadCompletionSignal();
        var periodGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delays = new ConcurrentQueue<TimeSpan>();

        using var service = new AutoSyncHostedService(
            _ctx.Orcestrator,
            signal,
            preferences,
            NullLogger<AutoSyncHostedService>.Instance,
            (delay, token) =>
            {
                delays.Enqueue(delay);
                delayCalled.TrySetResult();
                return periodGate.Task.WaitAsync(token);
            });

        await service.StartAsync(CancellationToken.None);
        signal.Complete();

        await delayCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var callsWhileDisabled = _sourceType.CallCount;

        preferences.AutoSyncEnabled = true;
        periodGate.TrySetResult();

        await _sourceType.WaitForCallsAsync(1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(callsWhileDisabled, Is.EqualTo(0));
            Assert.That(_sourceType.CallCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(delays, Does.Contain(TimeSpan.FromHours(1)));
        }
    }

    [Test]
    public async Task StopAsync_ВоВремяСтартовойПаузы_ОтменяетСервисБезСинхронизации()
    {
        var preferences = new ShellPreferences(_ctx.Settings)
        {
            AutoSyncEnabled = true,
            AutoSyncStartupDelaySeconds = 60,
        };
        var signal = new PreloadCompletionSignal();
        var startupDelayStarted = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var service = new AutoSyncHostedService(
            _ctx.Orcestrator,
            signal,
            preferences,
            NullLogger<AutoSyncHostedService>.Instance,
            async (delay, token) =>
            {
                startupDelayStarted.TrySetResult(delay);

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    delayCancelled.TrySetResult();
                    throw;
                }
            });

        await service.StartAsync(CancellationToken.None);
        signal.Complete();

        var observedDelay = await startupDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);
        await delayCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observedDelay, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(_sourceType.CallCount, Is.EqualTo(0));
        }
    }

    [Test]
    public void ShellPreferences_ЗажимаетСтартовуюПаузуВДиапазонеОт0До3600()
    {
        var preferences = new ShellPreferences(_ctx.Settings);
        var defaultEnabled = preferences.AutoSyncEnabled;
        var defaultDelay = preferences.AutoSyncStartupDelaySeconds;

        preferences.AutoSyncStartupDelaySeconds = -15;
        var clampedLow = preferences.AutoSyncStartupDelaySeconds;

        preferences.AutoSyncStartupDelaySeconds = 9999;
        var clampedHigh = preferences.AutoSyncStartupDelaySeconds;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(defaultEnabled, Is.True);
            Assert.That(defaultDelay, Is.EqualTo(60));
            Assert.That(clampedLow, Is.EqualTo(0));
            Assert.That(clampedHigh, Is.EqualTo(3600));
            Assert.That(_ctx.Settings.GetStringValue(ShellPreferences.AutoSyncStartupDelaySecondsKey), Is.EqualTo("3600"));
        }
    }

    private sealed class AutoSyncProbeSourceType : ISourceType
    {
        private int _callCount;
        private TaskCompletionSource? _waiter;
        private int _targetCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public string Name => "autosync-probe";

        public SyncDirection ChannelType => SyncDirection.OnlyDownload;

        public IEnumerable<SourceSettings> SettingsKeys => [];

        public Task WaitForCallsAsync(int count, TimeSpan timeout)
        {
            if (Volatile.Read(ref _callCount) >= count)
            {
                return Task.CompletedTask;
            }

            _targetCount = count;
            _waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);

            if (Volatile.Read(ref _callCount) >= count)
            {
                _waiter.TrySetResult();
            }

            return _waiter.Task.WaitAsync(timeout);
        }

        public async IAsyncEnumerable<MediaDto> GetMedia(
            Dictionary<string, string> settings,
            bool isFull,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _callCount);

            if (_waiter is { } waiter && current >= _targetCount)
            {
                waiter.TrySetResult();
            }

            await Task.CompletedTask;
            yield break;
        }

        public Task<MediaDto?> GetMediaByIdAsync(string externalId, Dictionary<string, string> settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MediaDto> DownloadAsync(string videoId, Dictionary<string, string> settings, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UploadResult> UploadAsync(MediaDto media, Dictionary<string, string> settings, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UploadResult> UpdateAsync(string externalId, MediaDto tempMedia, Dictionary<string, string> settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string externalId, Dictionary<string, string> settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool IsAuthenticated(Dictionary<string, string> settings) => true;

        public Task AuthenticateAsync(Dictionary<string, string> settings, IAuthUI ui, CancellationToken ct) => Task.CompletedTask;
    }
}
