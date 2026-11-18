using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class TransfersSkipTests
{
    private TestDomainContext _ctx = null!;
    private ISourceType _pluginA = null!;
    private ISourceType _pluginB = null!;
    private ISourceType _pluginC = null!;
    private TransfersViewModel _vm = null!;

    [SetUp]
    public void SetUp()
    {
        _pluginA = Substitute.For<ISourceType>();
        _pluginB = Substitute.For<ISourceType>();
        _pluginC = Substitute.For<ISourceType>();

        _ctx = new([new PluginA(_pluginA), new PluginB(_pluginB), new PluginC(_pluginC)]);

        var s1 = CreateSource("s1", "YouTube", "TypeA");
        var s2 = CreateSource("s2", "RuTube", "TypeB");
        var s3 = CreateSource("s3", "VK", "TypeC");
        _ctx.Database.GetCollection<Source>("sources").InsertBulk([s1, s2, s3]);
        _ctx.Database.GetCollection<SourceSyncRelation>("source_relations").InsertBulk([
            new() { FromId = s1.Id, ToId = s2.Id },
            new() { FromId = s2.Id, ToId = s3.Id },
        ]);

        _ctx.Database.GetCollection<Media>("medias").Insert(new Media
        {
            Id = "m1",
            Title = "Запись эфира",
            Sources =
            [
                new() { SourceId = "s1", ExternalId = "yt-live-1", Status = MediaStatus.Ok, Title = "Запись эфира" },
            ],
        });

        _pluginA.DownloadAsync("yt-live-1", Arg.Any<Dictionary<string, string>>(), Arg.Any<IProgress<DownloadProgress>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MediaDownloadSkippedException("Стрим отключён"));

        _vm = new(
            _ctx.Orcestrator,
            _ctx.SyncPlanner,
            _ctx.SyncRetryRunner,
            _ctx.ActionHolder,
            _ctx.Settings,
            _ctx.Navigation,
            NullLogger<TransfersViewModel>.Instance,
            _ctx.Dispatcher,
            _ctx.RenderProbe);
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task ExecuteSelectedCommand_ПриПропускеКорневогоШага_СнимаетВыборПотомковИНеЖдётПаузу()
    {
        _vm.UploadIntervalEnabled = true;
        _vm.UploadIntervalMinutesText = "60";

        await _vm.RebuildPlanCommand.ExecuteAsync(null);
        _vm.SelectAllCommand.Execute(null);

        var root = _vm.Groups[0].Children[0];
        var child = root.Children[0];

        await _vm.ExecuteSelectedCommand.ExecuteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.StatusKind, Is.EqualTo(TransferStatusKind.Skipped));
            Assert.That(root.StatusTail, Is.EqualTo("[Пропущено]"));
            Assert.That(root.ErrorTooltip, Is.EqualTo("Стрим отключён"));
            Assert.That(root.IsSelected, Is.False);
            Assert.That(child.IsSelected, Is.False);
            Assert.That(child.StatusKind, Is.EqualTo(TransferStatusKind.Pending));
            Assert.That(_vm.SelectedRoots, Is.EqualTo(0));
            Assert.That(_vm.SelectedSteps, Is.EqualTo(0));
            Assert.That(_vm.RunStepsFailed, Is.EqualTo(0));
            Assert.That(_vm.RunStepsCompleted, Is.EqualTo(1));
            Assert.That(_vm.StatusText, Does.Contain("пропущено: 1"));
            Assert.That(_vm.Journal.Any(j => j.Text.Contains("[Пропущено]")), Is.True);
        }

        await _pluginB.DidNotReceive().UploadAsync(
            Arg.Any<MediaDto>(),
            Arg.Any<Dictionary<string, string>>(),
            Arg.Any<IProgress<UploadProgress>?>(),
            Arg.Any<CancellationToken>());
        await _pluginC.DidNotReceive().UploadAsync(
            Arg.Any<MediaDto>(),
            Arg.Any<Dictionary<string, string>>(),
            Arg.Any<IProgress<UploadProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunSingleStepAsync_ПриПропуске_ЗавершаетсяСтатусомПропущено()
    {
        await _vm.RebuildPlanCommand.ExecuteAsync(null);
        var root = _vm.Groups[0].Children[0];

        await _vm.RunSingleStepCommand.ExecuteAsync(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.StatusKind, Is.EqualTo(TransferStatusKind.Skipped));
            Assert.That(root.StatusTail, Is.EqualTo("[Пропущено]"));
            Assert.That(_vm.StatusText, Is.EqualTo("Пропущено"));
            Assert.That(_vm.RunStepsCompleted, Is.EqualTo(1));
            Assert.That(_vm.RunStepsFailed, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task EnsureLoadedAsync_ПриПовторномОткрытииБезИзменений_ОставляетПланАктуальным()
    {
        await _vm.EnsureLoadedAsync();
        _vm.SelectAllCommand.Execute(null);

        await _vm.EnsureLoadedAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.IsPlanStale, Is.False);
            Assert.That(_vm.ExecuteSelectedCommand.CanExecute(null), Is.True);
        }
    }

    [Test]
    public async Task ExecuteSelectedCommand_ПослеИзмененияСвязей_ТребуетПересборку()
    {
        await _vm.EnsureLoadedAsync();
        _vm.SelectAllCommand.Execute(null);

        _ctx.Orcestrator.RemoveRelation("s2", "s3");
        await _vm.EnsureLoadedAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.IsPlanStale, Is.True);
            Assert.That(_vm.ExecuteSelectedCommand.CanExecute(null), Is.False);
            Assert.That(_vm.RunSingleStepCommand.CanExecute(_vm.Groups[0].Children[0]), Is.False);
        }

        await _vm.RebuildPlanCommand.ExecuteAsync(null);
        _vm.SelectAllCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.IsPlanStale, Is.False);
            Assert.That(_vm.ExecuteSelectedCommand.CanExecute(null), Is.True);
            Assert.That(_vm.PlanSteps, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task RunSingleStepCommand_ПослеОстановкиБезИзменений_УведомляетОДоступности()
    {
        await _vm.RebuildPlanCommand.ExecuteAsync(null);
        var node = _vm.Groups[0].Children[0];
        var availability = new List<bool>();
        _vm.RunSingleStepCommand.CanExecuteChanged += (_, _) =>
            availability.Add(_vm.RunSingleStepCommand.CanExecute(node));

        _vm.IsRunning = true;
        _vm.IsRunning = false;

        Assert.That(availability, Is.EqualTo(new[] { false, true }));
    }

    private static Source CreateSource(string id, string name, string typeId) =>
        new()
        {
            Id = id,
            TypeId = typeId,
            Settings = new() { ["_system_name"] = name },
        };

    private abstract class DelegatingPlugin(string name, ISourceType inner) : ISourceType
    {
        public string Name => name;
        public SyncDirection ChannelType => SyncDirection.Full;
        public IEnumerable<SourceSettings> SettingsKeys => [];
        public IAsyncEnumerable<MediaDto> GetMedia(Dictionary<string, string> settings, bool isFull, CancellationToken cancellationToken = default) => inner.GetMedia(settings, isFull, cancellationToken);
        public Task<MediaDto?> GetMediaByIdAsync(string externalId, Dictionary<string, string> settings, CancellationToken cancellationToken = default) => inner.GetMediaByIdAsync(externalId, settings, cancellationToken);
        public Task<MediaDto> DownloadAsync(string videoId, Dictionary<string, string> settings, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) => inner.DownloadAsync(videoId, settings, progress, cancellationToken);
        public Task<UploadResult> UploadAsync(MediaDto media, Dictionary<string, string> settings, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default) => inner.UploadAsync(media, settings, progress, cancellationToken);
        public Task<UploadResult> UpdateAsync(string externalId, MediaDto tempMedia, Dictionary<string, string> settings, CancellationToken cancellationToken = default) => inner.UpdateAsync(externalId, tempMedia, settings, cancellationToken);
        public Task DeleteAsync(string externalId, Dictionary<string, string> settings, CancellationToken cancellationToken = default) => inner.DeleteAsync(externalId, settings, cancellationToken);
    }

    private sealed class PluginA(ISourceType inner) : DelegatingPlugin("TypeA", inner);
    private sealed class PluginB(ISourceType inner) : DelegatingPlugin("TypeB", inner);
    private sealed class PluginC(ISourceType inner) : DelegatingPlugin("TypeC", inner);
}
