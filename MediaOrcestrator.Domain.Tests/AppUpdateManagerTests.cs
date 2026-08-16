using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
public class AppUpdateManagerTests
{
    private static ReleaseInfo NewerRelease()
    {
        return new()
        {
            TagName = "v999.0.0",
            AssetUrl = "https://example.test/MediaOrcestrator-v999.0.0.zip",
            AssetName = "MediaOrcestrator-v999.0.0.zip",
            AssetSize = 1024,
        };
    }

    private static AppUpdateManager CreateManager(IReleaseProvider provider, Func<string> updateRepo)
    {
        return new(provider, updateRepo, "MediaOrcestrator", NullLogger<AppUpdateManager>.Instance);
    }

    [TestCase("owner/alpha", "owner/alpha", 1)]
    [TestCase("owner/alpha", "owner/beta", 2)]
    public async Task Проверка_обновлений_кэшируется_по_текущему_репозиторию(string first, string second, int expectedProviderCalls)
    {
        var provider = Substitute.For<IReleaseProvider>();
        provider.GetLatestReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<ReleaseInfo?>(NewerRelease()));

        var repo = first;
        var manager = CreateManager(provider, () => repo);

        var firstUpdate = await manager.CheckForUpdateAsync();
        repo = second;
        var secondUpdate = await manager.CheckForUpdateAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstUpdate, Is.Not.Null);
            Assert.That(secondUpdate, Is.Not.Null);
            Assert.That(provider.ReceivedCalls().Count(), Is.EqualTo(expectedProviderCalls));
        }

        await provider.Received(1).GetLatestReleaseAsync(second, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestCase("MediaOrcestrator", "MediaOrcestrator-v*.zip")]
    [TestCase("MediaOrcestrator-Wpf", "MediaOrcestrator-Wpf-v*.zip")]
    public async Task Раннер_ищет_только_ассет_своего_канала(string assetPrefix, string expectedPattern)
    {
        var provider = Substitute.For<IReleaseProvider>();
        provider.GetLatestReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<ReleaseInfo?>(NewerRelease()));

        var manager = new AppUpdateManager(provider, () => "owner/alpha", assetPrefix, NullLogger<AppUpdateManager>.Instance);

        await manager.CheckForUpdateAsync();

        await provider.Received(1).GetLatestReleaseAsync("owner/alpha", expectedPattern, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Пустой_ответ_провайдера_даёт_ошибку_проверки_и_не_кэшируется()
    {
        var provider = Substitute.For<IReleaseProvider>();
        provider.GetLatestReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<ReleaseInfo?>(null));

        var manager = CreateManager(provider, () => "owner/alpha");

        var update = await manager.CheckForUpdateAsync();
        var error = manager.LastCheckError;
        await manager.CheckForUpdateAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(update, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(provider.ReceivedCalls().Count(), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Релиз_без_ассета_считается_актуальной_версией_без_ошибки()
    {
        var provider = Substitute.For<IReleaseProvider>();
        provider.GetLatestReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<ReleaseInfo?>(new() { TagName = "v999.0.0" }));

        var manager = CreateManager(provider, () => "owner/alpha");

        var update = await manager.CheckForUpdateAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(update, Is.Null);
            Assert.That(manager.LastCheckError, Is.Null);
        }
    }

    [Test]
    public async Task Отмена_проверки_пробрасывается_и_не_помечается_ошибкой()
    {
        var provider = Substitute.For<IReleaseProvider>();
        provider.GetLatestReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var manager = CreateManager(provider, () => "owner/alpha");

        Exception? caught = null;

        try
        {
            await manager.CheckForUpdateAsync(new CancellationToken(true));
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(caught, Is.InstanceOf<OperationCanceledException>());
            Assert.That(manager.LastCheckError, Is.Null);
        }
    }
}
