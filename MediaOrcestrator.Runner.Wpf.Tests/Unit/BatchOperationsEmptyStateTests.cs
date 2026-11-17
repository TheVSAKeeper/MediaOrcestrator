using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class BatchOperationsEmptyStateTests
{
    private TestDomainContext _ctx = null!;

    [SetUp]
    public void SetUp()
    {
        var pluginA = Substitute.For<ISourceType>();
        var pluginB = Substitute.For<ISourceType>();
        _ctx = new([new PluginA(pluginA), new PluginB(pluginB)]);
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public void BatchPreview_РазличаетПустойНаборИПустоеПересечениеПоПлощадкам()
    {
        var s1 = CreateSource("s1", "YouTube", "TypeA");
        var s2 = CreateSource("s2", "RuTube", "TypeB");
        _ctx.Database.GetCollection<Source>("sources").InsertBulk([s1, s2]);

        var vm = new BatchPreviewSubViewModel(
            _ctx.Orcestrator,
            _ctx.BatchPreviewService,
            _ctx.ActionHolder,
            _ctx.Dialogs,
            _ctx.Settings,
            _ctx.CoverGenerator,
            _ctx.CoverTemplateStore,
            _ctx.CoverEditorWindowService,
            NullLogger<BatchPreviewSubViewModel>.Instance,
            _ctx.FilePicker);

        var initialHasMedias = vm.HasMedias;
        var initialIsSetEmpty = vm.IsSetEmpty;
        var initialIsResultEmpty = vm.IsResultEmpty;

        var media = new Media
        {
            Id = "m1",
            Title = "Ролик",
            Sources =
            [
                new() { SourceId = s1.Id, ExternalId = "ext-1", Status = MediaStatus.Ok, Title = "Ролик" },
                new() { SourceId = s2.Id, ExternalId = "ext-2", Status = MediaStatus.Ok, Title = "Ролик" },
            ],
        };

        vm.LoadMedia([media]);
        var loadedHasMedias = vm.HasMedias;
        var loadedIsResultEmpty = vm.IsResultEmpty;

        vm.SelectNoTargetsCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(initialHasMedias, Is.False);
            Assert.That(initialIsSetEmpty, Is.True);
            Assert.That(initialIsResultEmpty, Is.True);
            Assert.That(loadedHasMedias, Is.True);
            Assert.That(loadedIsResultEmpty, Is.False);
            Assert.That(vm.HasMedias, Is.True);
            Assert.That(vm.IsSetEmpty, Is.False);
            Assert.That(vm.Rows, Is.Empty);
            Assert.That(vm.IsResultEmpty, Is.True);
        }
    }

    [Test]
    public void BatchRename_ПриЗагрузкеИОчисткеНабора_ОбновляетIsResultEmpty()
    {
        var vm = new BatchRenameSubViewModel(
            _ctx.Orcestrator,
            _ctx.BatchRenameService,
            _ctx.ActionHolder,
            _ctx.Dialogs,
            _ctx.Settings,
            _ctx.Dispatcher,
            NullLogger<BatchRenameSubViewModel>.Instance);

        var initialEmpty = vm.IsResultEmpty;

        vm.LoadMedia([new Media { Id = "m1", Title = "Серия 1" }]);
        var loadedEmpty = vm.IsResultEmpty;
        var loadedSetEmpty = vm.IsSetEmpty;

        vm.ClearMediaCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(initialEmpty, Is.True);
            Assert.That(loadedEmpty, Is.False);
            Assert.That(loadedSetEmpty, Is.False);
            Assert.That(vm.IsResultEmpty, Is.True);
            Assert.That(vm.IsSetEmpty, Is.True);
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
