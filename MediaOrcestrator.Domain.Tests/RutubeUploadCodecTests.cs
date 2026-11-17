using MediaOrcestrator.Modules;
using MediaOrcestrator.Rutube;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
file sealed class RutubeUploadCodecTests
{
    private string _tempDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RutubeCodecTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [TestCase("av1", "h264", true, false)]
    [TestCase("AV1", "h264", true, false)]
    [TestCase("vp9", null, false, true)]
    [TestCase("VP9", "h264", false, true)]
    public async Task Загрузка_для_av1_и_vp9_игнорирует_метаданные_и_транскодирует_в_h264(
        string probedCodec,
        string? metadataCodec,
        bool expectsAv1,
        bool expectsVp9)
    {
        var sourceFile = CreateNonEmptyFile("source.mp4", 128);
        var transcoder = new FakeTranscoder(probedCodec, transcodeSucceeds: true, writeOutput: true);
        var gateway = new FakeUploadGateway();
        var channel = CreateChannel(transcoder, gateway);
        var media = CreateMedia(sourceFile, metadataCodec, duration: "00:00:12");

        var result = await channel.UploadAsync(media, CreateSettings());

        var expectedTranscodedPath = Path.Combine(_tempDir, "media.transcoded.mp4");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status.Id, Is.EqualTo(MediaStatus.Ok));
            Assert.That(transcoder.ProbedPaths, Is.EqualTo(new[] { sourceFile }));
            Assert.That(transcoder.Av1Calls, Is.EqualTo(expectsAv1 ? 1 : 0));
            Assert.That(transcoder.Vp9Calls, Is.EqualTo(expectsVp9 ? 1 : 0));
            Assert.That(transcoder.LastDuration, Is.EqualTo(TimeSpan.FromSeconds(12)));
            Assert.That(gateway.UploadedFilePaths, Is.EqualTo(new[] { expectedTranscodedPath }));
            Assert.That(media.TempDataPath, Is.EqualTo(expectedTranscodedPath));
        }
    }

    [TestCase("h264")]
    [TestCase("hevc")]
    public async Task Загрузка_для_поддерживаемого_кодека_идёт_без_транскодирования(string probedCodec)
    {
        var sourceFile = CreateNonEmptyFile("source.mp4", 64);
        var transcoder = new FakeTranscoder(probedCodec, transcodeSucceeds: true, writeOutput: true);
        var gateway = new FakeUploadGateway();
        var channel = CreateChannel(transcoder, gateway);
        var media = CreateMedia(sourceFile, metadataCodec: "vp9");

        var result = await channel.UploadAsync(media, CreateSettings());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status.Id, Is.EqualTo(MediaStatus.Ok));
            Assert.That(transcoder.ProbedPaths, Is.EqualTo(new[] { sourceFile }));
            Assert.That(transcoder.Av1Calls, Is.EqualTo(0));
            Assert.That(transcoder.Vp9Calls, Is.EqualTo(0));
            Assert.That(gateway.UploadedFilePaths, Is.EqualTo(new[] { sourceFile }));
            Assert.That(media.TempDataPath, Is.EqualTo(sourceFile));
        }
    }

    [TestCase(null)]
    [TestCase("")]
    public async Task Загрузка_без_видеодорожки_бросает_NonRetriableException(string? probedCodec)
    {
        var sourceFile = CreateNonEmptyFile("audio-only.mp4", 32);
        var transcoder = new FakeTranscoder(probedCodec, transcodeSucceeds: true, writeOutput: true);
        var gateway = new FakeUploadGateway();
        var channel = CreateChannel(transcoder, gateway);
        var media = CreateMedia(sourceFile, metadataCodec: "h264");

        var error = await CaptureAsync(() => channel.UploadAsync(media, CreateSettings()));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error, Is.TypeOf<NonRetriableException>());
            Assert.That(transcoder.Av1Calls, Is.EqualTo(0));
            Assert.That(transcoder.Vp9Calls, Is.EqualTo(0));
            Assert.That(gateway.UploadedFilePaths, Is.Empty);
        }
    }

    [TestCase("av1", false, false, "AV1")]
    [TestCase("vp9", false, false, "VP9")]
    [TestCase("av1", true, false, "AV1")]
    public async Task Загрузка_при_сбое_транскодирования_бросает_NonRetriableException_с_именем_кодека(
        string probedCodec,
        bool transcodeSucceeds,
        bool writeOutput,
        string expectedCodecLabel)
    {
        var sourceFile = CreateNonEmptyFile("source.mp4", 64);
        var transcoder = new FakeTranscoder(probedCodec, transcodeSucceeds, writeOutput);
        var gateway = new FakeUploadGateway();
        var channel = CreateChannel(transcoder, gateway);
        var media = CreateMedia(sourceFile, metadataCodec: "h264");

        var error = await CaptureAsync(() => channel.UploadAsync(media, CreateSettings()));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error, Is.TypeOf<NonRetriableException>());
            Assert.That(error!.Message, Does.Contain(expectedCodecLabel));
            Assert.That(gateway.UploadedFilePaths, Is.Empty);
        }
    }

    private string CreateNonEmptyFile(string name, int bytes)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static MediaDto CreateMedia(string filePath, string? metadataCodec, string? duration = null)
    {
        List<MetadataItem> metadata = [];
        if (metadataCodec is not null)
        {
            metadata.Add(new() { Key = "VideoCodec", Value = metadataCodec });
        }

        if (duration is not null)
        {
            metadata.Add(new() { Key = "Duration", Value = duration });
        }

        return new()
        {
            Id = "m-1",
            Title = "Test",
            Description = "Desc",
            TempDataPath = filePath,
            Metadata = metadata,
        };
    }

    private static Dictionary<string, string> CreateSettings()
    {
        return new()
        {
            ["category_id"] = "13",
        };
    }

    private static RutubeChannel CreateChannel(FakeTranscoder transcoder, FakeUploadGateway gateway)
    {
        return new(
            NullLogger<RutubeChannel>.Instance,
            Substitute.For<IRutubeServiceFactory>(),
            Substitute.For<IToolPathProvider>(),
            transcoder,
            gateway);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class FakeTranscoder(string? codec, bool transcodeSucceeds, bool writeOutput)
        : RutubeChannel.IRutubeTranscoder
    {
        public List<string> ProbedPaths { get; } = [];
        public int Av1Calls { get; private set; }
        public int Vp9Calls { get; private set; }
        public TimeSpan LastDuration { get; private set; }

        public Task<string?> GetVideoCodecAsync(string filePath, CancellationToken cancellationToken)
        {
            ProbedPaths.Add(filePath);
            return Task.FromResult(codec);
        }

        public Task<bool> TranscodeVp9ToH264Async(
            string inputPath,
            string outputPath,
            TimeSpan totalDuration,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            Vp9Calls++;
            LastDuration = totalDuration;
            if (writeOutput)
            {
                File.WriteAllBytes(outputPath, [1, 2, 3, 4]);
            }

            return Task.FromResult(transcodeSucceeds);
        }

        public Task<bool> TranscodeAv1ToH264Async(
            string inputPath,
            string outputPath,
            TimeSpan totalDuration,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            Av1Calls++;
            LastDuration = totalDuration;
            if (writeOutput)
            {
                File.WriteAllBytes(outputPath, [1, 2, 3, 4]);
            }

            return Task.FromResult(transcodeSucceeds);
        }
    }

    private sealed class FakeUploadGateway : RutubeChannel.IRutubeUploadGateway
    {
        public List<string> UploadedFilePaths { get; } = [];

        public Task<UploadResult> UploadVideoAsync(
            Dictionary<string, string> settings,
            string filePath,
            string title,
            string description,
            string categoryId,
            string? thumbnailPath,
            DateTime? publishAt,
            long? uploadBytesPerSecond,
            IProgress<double>? uploadProgress,
            CancellationToken cancellationToken)
        {
            UploadedFilePaths.Add(filePath);
            return Task.FromResult(new UploadResult
            {
                Status = MediaStatusHelper.Ok(),
                Id = "rutube-1",
            });
        }
    }
}
