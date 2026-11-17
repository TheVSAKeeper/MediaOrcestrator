using LiteDB;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
public sealed class OrcestratorDisabledSourcesSyncTests
{
    private const string TagKey = "tag";

    private LiteDatabase _database = null!;
    private ActionHolder _actionHolder = null!;
    private CountingSourceType _sourceType = null!;
    private Orcestrator _orcestrator = null!;

    [SetUp]
    public void SetUp()
    {
        _database = new(":memory:");
        _actionHolder = new(NullLogger<ActionHolder>.Instance);
        _sourceType = new();

        var pluginManager = new PluginManager([_sourceType], null!, NullLogger<PluginManager>.Instance);
        pluginManager.Init();

        var tempManager = new TempManager(Path.GetTempPath(), _database, NullLogger<TempManager>.Instance);

        _orcestrator = new(
            pluginManager,
            _database,
            tempManager,
            null!,
            _actionHolder,
            NullLogger<Orcestrator>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _database.Dispose();
    }

    [Test]
    public async Task GetStorageFullInfo_ПриNullFilterSource_ПропускаетОтключённыеИсточники()
    {
        var sources = _database.GetCollection<Source>("sources");
        sources.Insert(new Source { Id = "active-1", TypeId = "counting", Settings = new() { [TagKey] = "a1" }, IsDisable = false });
        sources.Insert(new Source { Id = "disabled-1", TypeId = "counting", Settings = new() { [TagKey] = "d1" }, IsDisable = true });
        sources.Insert(new Source { Id = "active-2", TypeId = "counting", Settings = new() { [TagKey] = "a2" }, IsDisable = false });

        await _orcestrator.GetStorageFullInfo(false);

        var medias = _database.GetCollection<Media>("medias").FindAll().ToList();
        var parentCompleted = _actionHolder.CompletedSnapshot()
            .FirstOrDefault(a => a.Name == "Синхронизация источников");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_sourceType.SyncedTags, Is.EquivalentTo(new[] { "a1", "a2" }));
            Assert.That(medias, Has.Count.EqualTo(2));
            Assert.That(parentCompleted, Is.Not.Null);
            Assert.That(parentCompleted!.ProgressMax, Is.EqualTo(2));
            Assert.That(parentCompleted.Status, Is.EqualTo("Готово: 2 из 2 источников"));
        }
    }

    [Test]
    public async Task GetStorageFullInfo_КогдаВсеИсточникиОтключены_ЗавершаетДействиеБезВызоваПлагинов()
    {
        var sources = _database.GetCollection<Source>("sources");
        sources.Insert(new Source { Id = "disabled-1", TypeId = "counting", Settings = new() { [TagKey] = "d1" }, IsDisable = true });
        sources.Insert(new Source { Id = "disabled-2", TypeId = "counting", Settings = new() { [TagKey] = "d2" }, IsDisable = true });

        await _orcestrator.GetStorageFullInfo(false);

        var parentCompleted = _actionHolder.CompletedSnapshot()
            .FirstOrDefault(a => a.Name == "Синхронизация источников");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_sourceType.SyncedTags, Is.Empty);
            Assert.That(_actionHolder.ActiveCount, Is.EqualTo(0));
            Assert.That(parentCompleted, Is.Not.Null);
            Assert.That(parentCompleted!.State, Is.EqualTo(ActionState.Succeeded));
            Assert.That(parentCompleted.Status, Is.EqualTo("Нет активных источников"));
        }
    }

    [Test]
    public async Task GetStorageFullInfo_ПриЯвномFilterSource_СинхронизируетОтключённыйИсточник()
    {
        var disabled = new Source
        {
            Id = "disabled-1",
            TypeId = "counting",
            Settings = new() { [TagKey] = "d1" },
            IsDisable = true,
        };
        _database.GetCollection<Source>("sources").Insert(disabled);

        await _orcestrator.GetStorageFullInfo(false, filterSource: disabled);

        var medias = _database.GetCollection<Media>("medias").FindAll().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_sourceType.SyncedTags, Is.EqualTo(new[] { "d1" }));
            Assert.That(medias, Has.Count.EqualTo(1));
        }
    }

    private sealed class CountingSourceType : ISourceType
    {
        public ConcurrentBag<string> SyncedTags { get; } = [];

        public string Name => "counting";

        public SyncDirection ChannelType => SyncDirection.OnlyDownload;

        public IEnumerable<SourceSettings> SettingsKeys => [];

        public async IAsyncEnumerable<MediaDto> GetMedia(
            Dictionary<string, string> settings,
            bool isFull,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var tag = settings[TagKey];
            SyncedTags.Add(tag);

            yield return new()
            {
                Id = $"{tag}-item",
                Title = $"Item {tag}",
                Description = string.Empty,
            };

            await Task.CompletedTask;
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
