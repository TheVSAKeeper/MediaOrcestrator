using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class TransfersPlanErrorStateTests
{
    private TestDomainContext _ctx = null!;
    private TransfersViewModel _vm = null!;

    [SetUp]
    public void SetUp()
    {
        var pluginA = Substitute.For<ISourceType>();
        var pluginB = Substitute.For<ISourceType>();
        _ctx = new([new PluginA(pluginA), new PluginB(pluginB)]);

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
    public async Task RebuildPlanCommand_ПриСбоеЗагрузкиИзБД_ОчищаетСтарыйПланИСтавитСостояниеОшибки()
    {
        var s1 = CreateSource("s1", "YouTube", "TypeA");
        var s2 = CreateSource("s2", "RuTube", "TypeB");
        _ctx.Database.GetCollection<Source>("sources").InsertBulk([s1, s2]);
        _ctx.Database.GetCollection<SourceSyncRelation>("source_relations").Insert(
            new SourceSyncRelation { FromId = s1.Id, ToId = s2.Id });
        _ctx.Database.GetCollection<Media>("medias").Insert(new Media
        {
            Id = "m1",
            Title = "Видео",
            Sources =
            [
                new() { SourceId = s1.Id, ExternalId = "ext-1", Status = MediaStatus.Ok, Title = "Видео" },
            ],
        });

        await _vm.RebuildPlanCommand.ExecuteAsync(null);
        Assert.That(_vm.HasPlan, Is.True);

        _ctx.Database.Dispose();
        await _vm.RebuildPlanCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.HasPlanError, Is.True);
            Assert.That(_vm.HasPlan, Is.False);
            Assert.That(_vm.IsPlanLoaded, Is.False);
            Assert.That(_vm.PlanRoots, Is.EqualTo(0));
            Assert.That(_vm.PlanSteps, Is.EqualTo(0));
            Assert.That(_vm.Groups, Is.Empty);
            Assert.That(_vm.EmptyIcon, Is.EqualTo(PackIconLucideKind.TriangleAlert));
            Assert.That(_vm.EmptyHeading, Is.EqualTo("Не удалось построить план"));
            Assert.That(_vm.EmptyHint, Is.Not.Null.And.Not.Empty);
            Assert.That(_vm.StatusText, Does.StartWith("Ошибка построения плана:"));
        }
    }

    [Test]
    public async Task RebuildPlanCommand_КогдаЕстьОшибкиПрогонаИПустоеДерево_НеПоказываетЗелёнуюГалочку()
    {
        var s1 = CreateSource("s1", "YouTube", "TypeA");
        var s2 = CreateSource("s2", "RuTube", "TypeB");
        _ctx.Database.GetCollection<Source>("sources").InsertBulk([s1, s2]);
        _ctx.Database.GetCollection<SourceSyncRelation>("source_relations").Insert(
            new SourceSyncRelation { FromId = s1.Id, ToId = s2.Id });

        _vm.SetErrorForAutomation(true);
        await _vm.RebuildPlanCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.HasRunErrors, Is.True);
            Assert.That(_vm.HasPlan, Is.False);
            Assert.That(_vm.EmptyIcon, Is.EqualTo(PackIconLucideKind.TriangleAlert));
            Assert.That(_vm.EmptyHeading, Is.EqualTo("В последнем прогоне были ошибки"));
            Assert.That(_vm.EmptyHint, Does.Contain("4"));
        }
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
}
