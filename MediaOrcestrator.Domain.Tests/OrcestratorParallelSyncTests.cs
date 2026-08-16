using LiteDB;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
public class OrcestratorParallelSyncTests
{
    private const int SourceCount = 8;
    private const int MediaPerSource = 40;

    private const string TagKey = "tag";

    private LiteDatabase _database = null!;
    private Orcestrator _orcestrator = null!;

    [SetUp]
    public void SetUp()
    {
        _database = new(":memory:");

        var sourceType = new TaggedSourceType();

        var pluginManager = new PluginManager([sourceType], null!, NullLogger<PluginManager>.Instance);
        pluginManager.Init();

        var tempManager = new TempManager(Path.GetTempPath(), _database, NullLogger<TempManager>.Instance);

        _orcestrator = new(pluginManager,
            _database,
            tempManager,
            null!,
            new(NullLogger<ActionHolder>.Instance),
            NullLogger<Orcestrator>.Instance);

        var sources = _database.GetCollection<Source>("sources");

        for (var i = 0; i < SourceCount; i++)
        {
            sources.Insert(new Source
            {
                Id = $"src-{i}",
                TypeId = "from",
                Settings = new() { [TagKey] = $"s{i}" },
            });
        }
    }

    [TearDown]
    public void TearDown()
    {
        _database.Dispose();
    }

    [Test]
    public async Task Синхронизация_нескольких_источников_сохраняет_все_медиа()
    {
        await _orcestrator.GetStorageFullInfo(false);

        var saved = _database.GetCollection<Media>("medias").FindAll().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved, Has.Count.EqualTo(SourceCount * MediaPerSource));
            Assert.That(saved.SelectMany(m => m.Sources).Select(s => s.SourceId + "|" + s.ExternalId).Distinct().Count(),
                Is.EqualTo(SourceCount * MediaPerSource));
        }
    }

    [Test]
    public async Task Повторная_синхронизация_не_плодит_дубли()
    {
        await _orcestrator.GetStorageFullInfo(false);

        var afterFirst = _database.GetCollection<Media>("medias").FindAll().ToList();
        var linksAfterFirst = afterFirst.Sum(m => m.Sources.Count);

        await _orcestrator.GetStorageFullInfo(false);

        var saved = _database.GetCollection<Media>("medias").FindAll().ToList();

        Assert.That(saved, Has.Count.EqualTo(SourceCount * MediaPerSource),
            $"после первого прогона медиа {afterFirst.Count}, ссылок {linksAfterFirst}; после второго медиа {saved.Count}");
    }

    private sealed class TaggedSourceType : ISourceType
    {
        public string Name => "from";

        public SyncDirection ChannelType => SyncDirection.OnlyDownload;

        public IEnumerable<SourceSettings> SettingsKeys => [];

        public async IAsyncEnumerable<MediaDto> GetMedia(
            Dictionary<string, string> settings,
            bool isFull,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var tag = settings[TagKey];

            for (var i = 0; i < MediaPerSource; i++)
            {
                yield return new()
                {
                    Id = $"{tag}-{i}",
                    Title = $"{tag}-{i}",
                    Description = string.Empty,
                };
            }

            await Task.CompletedTask;
        }

        public Task<MediaDto?> GetMediaByIdAsync(string externalId, Dictionary<string, string> settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<MediaDto> DownloadAsync(string videoId, Dictionary<string, string> settings, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<UploadResult> UploadAsync(MediaDto media, Dictionary<string, string> settings, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<UploadResult> UpdateAsync(string externalId, MediaDto tempMedia, Dictionary<string, string> settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(string externalId, Dictionary<string, string> settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public bool IsAuthenticated(Dictionary<string, string> settings)
        {
            return true;
        }

        public Task AuthenticateAsync(Dictionary<string, string> settings, IAuthUI ui, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }
}
