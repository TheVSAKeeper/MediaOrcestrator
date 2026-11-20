using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class AppUpdateDialogViewModelTests
{
    [TestCase(false, "Ошибка", LogLevel.Error)]
    [TestCase(true, "Отменено", LogLevel.Information)]
    public async Task Install_ПрерваннаяЗагрузка_РазличаетТаймаутИОтмену(bool userCancelled, string status, LogLevel level)
    {
        using var context = new TestDomainContext();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.ReleaseProvider.DownloadAssetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<IProgress<double>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                started.TrySetResult();
                return userCancelled
                    ? Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>())
                    : Task.FromException(new TaskCanceledException("Истекло время загрузки."));
            });
        var updateManager = new AppUpdateManager(context.ReleaseProvider, () => "", $"test-{Guid.NewGuid():N}",
            NullLogger<AppUpdateManager>.Instance);
        var logger = Substitute.For<ILogger>();
        var dialog = new AppUpdateDialogViewModel(updateManager, new("1.0.0", "https://example.invalid/update", "", 42),
            logger, context.ShellLauncher);

        var install = dialog.InstallCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (userCancelled)
        {
            dialog.CancelCommand.Execute(null);
        }
        await install.WaitAsync(TimeSpan.FromSeconds(5));

        var events = logger.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log)).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.StatusText, Is.EqualTo(status));
            Assert.That(events.Select(call => call.GetArguments()[0]), Is.EqualTo(new[] { level }));
        }
    }
}
