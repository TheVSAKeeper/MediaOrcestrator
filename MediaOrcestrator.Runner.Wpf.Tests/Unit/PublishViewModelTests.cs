using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SkiaSharp;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class PublishViewModelTests
{
    private TestDomainContext _ctx = null!;
    private ISourceType _uploadPlugin = null!;
    private PublishViewModel _vm = null!;

    [SetUp]
    public void SetUp()
    {
        _uploadPlugin = Substitute.For<ISourceType>();
        _uploadPlugin.Name.Returns("Youtube");
        _uploadPlugin.ChannelType.Returns(SyncDirection.Full);

        _ctx = new([_uploadPlugin]);
        _vm = new(
            _ctx.Orcestrator,
            _ctx.SyncRetryRunner,
            _ctx.SyncPlanner,
            _ctx.ActionHolder,
            _ctx.Dialogs,
            NullLogger<PublishViewModel>.Instance,
            _ctx.FilePicker,
            _ctx.CoverGenerator,
            _ctx.CoverTemplateStore,
            _ctx.CoverEditorWindowService,
            _ctx.TempManager);
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public void PublishCommand_БезОбязательныхПолейИПриПубликации_МеняетCanExecute()
    {
        var source = CreateSource("s1", "YouTube", "Youtube");

        var initialCanExecute = _vm.PublishCommand.CanExecute(null);

        _vm.SelectedSource = source;
        _vm.Title = "Ролик";
        var withoutVideo = _vm.PublishCommand.CanExecute(null);

        _vm.VideoPath = Path.Combine(_ctx.Root, "video.mp4");
        var readyToPublish = _vm.PublishCommand.CanExecute(null);

        _vm.Title = "   ";
        var blankTitle = _vm.PublishCommand.CanExecute(null);

        _vm.Title = "Ролик";
        _vm.IsPublishing = true;
        var whilePublishing = _vm.PublishCommand.CanExecute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(initialCanExecute, Is.False);
            Assert.That(withoutVideo, Is.False);
            Assert.That(readyToPublish, Is.True);
            Assert.That(blankTitle, Is.False);
            Assert.That(whilePublishing, Is.True);
        }
    }

    [Test]
    public async Task VideoPath_ПриУстановке_ЗаполняетНазваниеИЧитаетСайдкарОписание()
    {
        var videoPath = Path.Combine(_ctx.Root, "Новый выпуск.mp4");
        await File.WriteAllTextAsync(videoPath, "video");
        await File.WriteAllTextAsync(Path.Combine(_ctx.Root, "Новый выпуск.description"), "  Описание из description  ");

        _vm.VideoPath = videoPath;
        await (_vm.PendingSidecarTask ?? Task.CompletedTask);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.Title, Is.EqualTo("Новый выпуск"));
            Assert.That(_vm.Description, Is.EqualTo("Описание из description"));
            Assert.That(_vm.DescriptionTemplates, Has.Count.EqualTo(1));
            Assert.That(_vm.DescriptionTemplates[0].IsFromVideoFile, Is.True);
        }

        var nextVideo = Path.Combine(_ctx.Root, "Другой ролик.mp4");
        await File.WriteAllTextAsync(nextVideo, "video2");
        await File.WriteAllTextAsync(Path.Combine(_ctx.Root, "Другой ролик.txt"), "Текст из txt");

        _vm.Title = "Авторское название";
        _vm.Description = string.Empty;
        _vm.VideoPath = nextVideo;
        await (_vm.PendingSidecarTask ?? Task.CompletedTask);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.Title, Is.EqualTo("Авторское название"));
            Assert.That(_vm.Description, Is.EqualTo("Текст из txt"));
        }
    }

    [Test]
    public async Task Reload_ИВыборИсточника_ЗаполняютПодсказкиИАвтозаполняютОписаниеПоТочномуНазванию()
    {
        var s1 = CreateSource("s1", "YouTube", "Youtube");
        var s2 = CreateSource("s2", "Другой", "Youtube");
        _ctx.Database.GetCollection<Source>("sources").Insert(s1);
        _ctx.Database.GetCollection<Source>("sources").Insert(s2);

        _ctx.Database.GetCollection<Media>("medias").Insert(new Media
        {
            Id = "m1",
            Title = "Первый",
            Sources =
            [
                new() { SourceId = "s1", ExternalId = "1", SortNumber = 1, Title = "Старый выпуск", Description = "Старое описание", Status = MediaStatus.Ok },
                new() { SourceId = "s2", ExternalId = "2", SortNumber = 2, Title = "Чужой выпуск", Description = "Чужое описание", Status = MediaStatus.Ok },
            ],
        });
        _ctx.Database.GetCollection<Media>("medias").Insert(new Media
        {
            Id = "m2",
            Title = "Второй",
            Sources =
            [
                new() { SourceId = "s1", ExternalId = "3", SortNumber = 10, Title = "Свежий выпуск", Description = "Свежее описание", Status = MediaStatus.Ok },
            ],
        });

        _vm.Reload();
        await (_vm.PendingSuggestionsTask ?? Task.CompletedTask);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.Sources, Has.Count.EqualTo(2));
            Assert.That(_vm.TitleSuggestions, Is.EqualTo(new[] { "Свежий выпуск", "Старый выпуск" }));
            Assert.That(_vm.DescriptionTemplates.Select(d => d.Text), Is.EqualTo(new[] { "Свежее описание", "Старое описание" }));
        }

        _vm.Title = "Старый выпуск";

        Assert.That(_vm.Description, Is.EqualTo("Старое описание"));
    }

    [Test]
    public async Task GenerateCoverFromTemplateCommand_ПриВалидномШаблоне_РендеритPngВоВременнуюПапку()
    {
        var templateBg = Path.Combine(_ctx.Root, "bg.png");
        CreateSamplePng(templateBg, 64, 36);

        _vm.Title = "Обзор #7";
        _vm.TemplatePicker.SelectedProfileName = "Основной";
        _vm.TemplatePicker.SelectedTemplate = new(
            templateBg,
            1,
            CoverNumberMode.TitleRegex,
            CoverTemplate.DefaultTitleRegex,
            [CoverTemplate.DefaultNumberLayer]);

        await _vm.GenerateCoverFromTemplateCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.CoverPath, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(_vm.CoverPath!), Is.True);
            Assert.That(_vm.CoverPath!, Does.StartWith(Path.Combine(_ctx.Paths.TempPath, "publish-covers")));
            Assert.That(_vm.IsStatusError, Is.False);
            Assert.That(_vm.StatusMessage, Does.Contain("№7"));
        }
    }

    [Test]
    public async Task PublishCommand_КогдаШагЦепочкиПропущен_ОстанавливаетЦепочкуБезОшибки()
    {
        var secondInner = Substitute.For<ISourceType>();
        var thirdInner = Substitute.For<ISourceType>();

        using var ctx = new TestDomainContext([_uploadPlugin, new RutubeTestPlugin(secondInner), new VkTestPlugin(thirdInner)]);
        var s1 = CreateSource("s1", "YouTube", "Youtube");
        var s2 = CreateSource("s2", "RuTube", "Rutube");
        var s3 = CreateSource("s3", "VK", "VkVideo");
        ctx.Database.GetCollection<Source>("sources").InsertBulk([s1, s2, s3]);
        ctx.Database.GetCollection<SourceSyncRelation>("source_relations").InsertBulk([
            new() { FromId = s1.Id, ToId = s2.Id },
            new() { FromId = s2.Id, ToId = s3.Id },
        ]);

        _uploadPlugin.UploadAsync(Arg.Any<MediaDto>(), Arg.Any<Dictionary<string, string>>(), Arg.Any<IProgress<UploadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new UploadResult { Status = MediaStatusHelper.Ok(), Id = "ext-1" });
        _uploadPlugin.DownloadAsync("ext-1", Arg.Any<Dictionary<string, string>>(), Arg.Any<IProgress<DownloadProgress>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MediaDownloadSkippedException("Стрим отключён"));

        var vm = new PublishViewModel(
            ctx.Orcestrator,
            ctx.SyncRetryRunner,
            ctx.SyncPlanner,
            ctx.ActionHolder,
            ctx.Dialogs,
            NullLogger<PublishViewModel>.Instance,
            ctx.FilePicker,
            ctx.CoverGenerator,
            ctx.CoverTemplateStore,
            ctx.CoverEditorWindowService,
            ctx.TempManager);

        var videoPath = Path.Combine(ctx.Root, "pub.mp4");
        await File.WriteAllTextAsync(videoPath, "video");
        vm.Reload();
        vm.SelectedSource = vm.Sources.First(s => s.Id == "s1");
        vm.Title = "Ролик";
        vm.VideoPath = videoPath;
        vm.RunChainAfterPublish = true;

        await vm.PublishCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.IsStatusError, Is.False);
            Assert.That(vm.StatusMessage, Does.Contain("пропущено"));
        }

        await secondInner.DidNotReceive().UploadAsync(
            Arg.Any<MediaDto>(),
            Arg.Any<Dictionary<string, string>>(),
            Arg.Any<IProgress<UploadProgress>?>(),
            Arg.Any<CancellationToken>());
    }

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

    private sealed class RutubeTestPlugin(ISourceType inner) : DelegatingPlugin("Rutube", inner);
    private sealed class VkTestPlugin(ISourceType inner) : DelegatingPlugin("VkVideo", inner);

    private static Source CreateSource(string id, string name, string typeId) =>
        new()
        {
            Id = id,
            TypeId = typeId,
            Settings = new() { ["_system_name"] = name },
        };

    private static void CreateSamplePng(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.DarkSlateGray);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
