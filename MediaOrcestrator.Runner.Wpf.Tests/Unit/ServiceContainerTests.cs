using KeepShell.Bootstrap;
using KeepShell.Diagnostics;
using KeepShell.Testing;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class ServiceContainerTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MediaOrcestrator.ContainerTests", Guid.NewGuid().ToString("N"));
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

    [Test]
    public void BuildServiceProvider_СоВсемиЗависимостями_ПроходитВалидациюИИспользуетLogsDirectory()
    {
        var logsDir = Path.Combine(_tempDir, "custom-logs");
        var paths = new Paths(
            Path.Combine(_tempDir, "plugins"),
            Path.Combine(_tempDir, "MyData.db"),
            Path.Combine(_tempDir, "temp"),
            Path.Combine(_tempDir, "state"));

        var loggingOptions = new KeepShellLoggingOptions { LogsDirectory = logsDir };

        using var provider = ServiceRegistration.BuildServiceProvider(
            paths,
            new MemorySettings(),
            new WpfLogSink(32),
            loggingOptions);

        var shell = provider.GetRequiredService<ShellViewModel>();
        var maintenance = provider.GetRequiredService<DatabaseMaintenanceService>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shell, Is.Not.Null);
            Assert.That(maintenance.LogsPath, Is.EqualTo(Path.GetFullPath(logsDir)));
        }
    }
}
