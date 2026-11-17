using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Windows.Documents;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class CommentsFilterPerfTests
{
    private TestDomainContext _ctx = null!;
    private FakeCommentSourceType _plugin = null!;

    [SetUp]
    public void SetUp()
    {
        _plugin = new();
        _ctx = new([_plugin]);
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task СменаКлиентскихФильтров_ПовторноИспользуетСнимокИЗаполняетRenderStages()
    {
        SeedSourceMediaAndComments(count: 4);
        var vm = CreateViewModel();

        await vm.EnsureLoadedAsync();
        await vm.WaitForPendingApplyAsync();

        var missesAfterLoad = vm.SnapshotCacheMisses;
        var hitsAfterLoad = vm.SnapshotCacheHits;
        var stagesAfterLoad = vm.RenderStages?.Select(s => s.Name).ToList() ?? [];

        _ctx.CommentsRepository.Upsert(new CommentRecord
        {
            Id = "s1|ext-1|c-late",
            SourceId = "s1",
            ExternalMediaId = "ext-1",
            ExternalCommentId = "c-late",
            AuthorName = "Поздний автор",
            Text = "Новый комментарий после кэша",
            PublishedAt = DateTime.UtcNow,
        });

        vm.SelectedReplyStatus = vm.ReplyStatusOptions.First(o => o.Key == CommentsReplyStatusFilter.All);
        await vm.WaitForPendingApplyAsync();

        vm.SelectedLayout = vm.LayoutOptions.First(o => o.Mode == CommentsLayoutMode.Grouped);
        await vm.WaitForPendingApplyAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missesAfterLoad, Is.EqualTo(1));
            Assert.That(hitsAfterLoad, Is.EqualTo(0));
            Assert.That(vm.SnapshotCacheMisses, Is.EqualTo(1));
            Assert.That(vm.SnapshotCacheHits, Is.EqualTo(2));
            Assert.That(vm.Items.OfType<CommentItemViewModel>().Any(i => i.Id == "s1|ext-1|c-late"), Is.False);
            Assert.That(_ctx.RenderProbe.FrameMeasures, Is.EqualTo(3));
            Assert.That(stagesAfterLoad, Does.Contain("Чтение БД"));
            Assert.That(stagesAfterLoad, Does.Contain("Индекс медиа"));
            Assert.That(stagesAfterLoad, Does.Contain("Сборка дерева"));
            Assert.That(stagesAfterLoad, Does.Contain("Обновление списка"));
            Assert.That(stagesAfterLoad, Does.Contain("Отрисовка кадра"));
        }
    }

    [Test]
    public async Task ИзменениеКлючаПоискаЛимитаИсточника_СбрасываетПопаданиеВСнимок()
    {
        SeedSourceMediaAndComments(count: 3);
        var vm = CreateViewModel();

        await vm.EnsureLoadedAsync();
        await vm.WaitForPendingApplyAsync();

        vm.SelectedReplyStatus = vm.ReplyStatusOptions.First(o => o.Key == CommentsReplyStatusFilter.All);
        await vm.WaitForPendingApplyAsync();

        vm.SearchText = "Комментарий 1";
        _ctx.Dispatcher.FireAllRunningTimers();
        await vm.WaitForPendingApplyAsync();
        var missesAfterSearch = vm.SnapshotCacheMisses;

        vm.Limit = 500;
        await vm.WaitForPendingApplyAsync();
        var missesAfterLimit = vm.SnapshotCacheMisses;

        vm.Sources[0].IsChecked = true;
        await vm.WaitForPendingApplyAsync();
        var missesAfterSource = vm.SnapshotCacheMisses;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missesAfterSearch, Is.EqualTo(2));
            Assert.That(missesAfterLimit, Is.EqualTo(3));
            Assert.That(missesAfterSource, Is.EqualTo(4));
        }
    }

    [Test]
    public async Task МутацияИЯвнаяПерезагрузка_ИнвалидируютКэшСнимка()
    {
        SeedSourceMediaAndComments(count: 2);
        var vm = CreateViewModel();

        await vm.EnsureLoadedAsync();
        await vm.WaitForPendingApplyAsync();

        vm.SelectedReplyStatus = vm.ReplyStatusOptions.First(o => o.Key == CommentsReplyStatusFilter.All);
        await vm.WaitForPendingApplyAsync();

        var item = vm.Items.OfType<CommentItemViewModel>().First();
        await vm.ExecuteLikeAsync(item, liked: true);

        vm.SelectedLayout = vm.LayoutOptions.First(o => o.Mode == CommentsLayoutMode.Grouped);
        await vm.WaitForPendingApplyAsync();
        var missesAfterLike = vm.SnapshotCacheMisses;

        vm.ReloadSourcesCommandCommand.Execute(null);
        await vm.WaitForPendingApplyAsync();
        var missesAfterReload = vm.SnapshotCacheMisses;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missesAfterLike, Is.EqualTo(2));
            Assert.That(missesAfterReload, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task ПостроениеДерева_ДляОдногоМедиа_ВызываетGetExternalUriОдинРаз()
    {
        SeedSourceMediaAndComments(count: 10);
        var vm = CreateViewModel();

        await vm.EnsureLoadedAsync();
        await vm.WaitForPendingApplyAsync();

        vm.SelectedReplyStatus = vm.ReplyStatusOptions.First(o => o.Key == CommentsReplyStatusFilter.All);
        await vm.WaitForPendingApplyAsync();

        Assert.That(_plugin.GetExternalUriCalls, Is.EqualTo(1));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void BuildInlines_ОбычныйТекстБезПоиска_ВозвращаетОдинRunБезРазбора()
    {
        var host = Substitute.For<ICommentItemHost>();

        var inlines = CommentTextRenderer.BuildInlines("Обычный комментарий без ссылок и упоминаний.", null, host);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inlines, Has.Count.EqualTo(1));
            Assert.That(inlines[0], Is.TypeOf<Run>());
            Assert.That(((Run)inlines[0]).Text, Is.EqualTo("Обычный комментарий без ссылок и упоминаний."));
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void BuildInlines_УпоминанияСсылкиИПоиск_СоздаётHyperlinkИПодсветку()
    {
        var host = Substitute.For<ICommentItemHost>();
        var text = "Привет [id42|Иван] и @ChannelName, ссылка https://example.com/watch.";

        var rich = CommentTextRenderer.BuildInlines(text, null, host);
        var highlighted = CommentTextRenderer.BuildInlines("Тестовый поиск внутри текста", "поиск", host);

        var hyperlinks = rich.OfType<Hyperlink>().ToList();
        var runs = highlighted.OfType<Run>().Select(r => r.Text).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hyperlinks, Has.Count.EqualTo(3));
            Assert.That(hyperlinks[0].NavigateUri?.ToString(), Is.EqualTo("https://vk.com/id42"));
            Assert.That(hyperlinks[1].NavigateUri?.ToString(), Is.EqualTo("https://www.youtube.com/@ChannelName"));
            Assert.That(hyperlinks[2].NavigateUri?.ToString(), Is.EqualTo("https://example.com/watch"));
            Assert.That(runs, Is.EqualTo(new[] { "Тестовый ", "поиск", " внутри текста" }));
        }
    }

    private CommentsViewModel CreateViewModel() =>
        new(
            _ctx.Orcestrator,
            _ctx.CommentsService,
            _ctx.ActionHolder,
            _ctx.Dialogs,
            _ctx.Settings,
            NullLogger<CommentsViewModel>.Instance,
            _ctx.ShellLauncher,
            _ctx.Dispatcher,
            _ctx.RenderProbe);

    private void SeedSourceMediaAndComments(int count)
    {
        var source = new Source
        {
            Id = "s1",
            TypeId = _plugin.Name,
            Settings = new() { ["_system_name"] = "Тестовый канал" },
        };
        _ctx.Database.GetCollection<Source>("sources").Insert(source);

        _ctx.Database.GetCollection<Media>("medias").Insert(new Media
        {
            Id = "m1",
            Title = "Видео 1",
            Sources =
            [
                new() { SourceId = "s1", ExternalId = "ext-1", Status = MediaStatus.Ok, Title = "Видео 1" },
            ],
        });

        var records = Enumerable.Range(1, count)
            .Select(i => new CommentRecord
            {
                Id = $"s1|ext-1|c-{i}",
                SourceId = "s1",
                ExternalMediaId = "ext-1",
                ExternalCommentId = $"c-{i}",
                AuthorName = $"Автор {i}",
                Text = $"Комментарий {i}",
                PublishedAt = DateTime.UtcNow.AddMinutes(-i),
            })
            .ToList();

        _ctx.CommentsRepository.UpsertMany(records);
    }

    private sealed class FakeCommentSourceType : ISourceType, ISupportsComments, ISupportsCommentLikes
    {
        public int GetExternalUriCalls { get; private set; }

        public string Name => "FakeCommentSource";

        public SyncDirection ChannelType => SyncDirection.OnlyDownload;

        public IEnumerable<SourceSettings> SettingsKeys => [];

        public Uri? GetExternalUri(string externalId, Dictionary<string, string> settings)
        {
            GetExternalUriCalls++;
            return new($"https://example.com/video/{externalId}");
        }

        public async IAsyncEnumerable<MediaDto> GetMedia(
            Dictionary<string, string> settings,
            bool isFull,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<MediaDto?> GetMediaByIdAsync(string externalId, Dictionary<string, string> settings, CancellationToken cancellationToken = default) =>
            Task.FromResult<MediaDto?>(null);

        public Task<MediaDto> DownloadAsync(string videoId, Dictionary<string, string> settings, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UploadResult> UploadAsync(MediaDto media, Dictionary<string, string> settings, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UploadResult> UpdateAsync(string externalId, MediaDto tempMedia, Dictionary<string, string> settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string externalId, Dictionary<string, string> settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async IAsyncEnumerable<CommentDto> GetCommentsAsync(
            string externalId,
            Dictionary<string, string> settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<int> LikeCommentAsync(string externalMediaId, string externalCommentId, Dictionary<string, string> settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(1);

        public Task<int> UnlikeCommentAsync(string externalMediaId, string externalCommentId, Dictionary<string, string> settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
