using KeepShell.Mcp;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Design;

public sealed class DesignSettingsViewModel : SettingsViewModel
{
    public DesignSettingsViewModel()
        : base(
            new DesignSettingsStore(),
            new Paths(
                @"C:\Sources\MediaOrcestrator\ModuleBuilds",
                @"C:\Sources\MediaOrcestrator\MyData.db",
                @"C:\Users\admin\AppData\Local\MediaOrcestrator\Temp",
                @"C:\Users\admin\AppData\Local\MediaOrcestrator\State"),
            new DesignDialogService(),
            new DesignAppLifetime(),
            NullLogger<SettingsViewModel>.Instance,
            new ShellPreferences(new DesignSettingsStore()),
            new ThemeViewModel(new DesignSettingsStore()),
            new UpdatePreferences(new DesignSettingsStore()),
            DesignUpdates.ViewModel,
            DesignMcp.Preferences,
            DesignMcp.Host,
            DesignMcp.Options,
            new DesignClipboardService(),
            new DesignShellLauncher(),
            new DesignFilePicker(),
            new ToastHostViewModel())
    {
        Paths[1].EditValue = @"D:\Data\MediaOrcestrator\MyData.db";
        Paths[2].EditValue = string.Empty;

        StatusMessage = "Сохранено. Перезапустите приложение, чтобы новые пути применились.";
    }
}

internal sealed class DesignSettingsStore : ISettingsStore
{
    public event EventHandler<string>? Changed
    {
        add { }
        remove { }
    }

    public event EventHandler<SettingsWriteFailedEventArgs>? WriteFailed
    {
        add { }
        remove { }
    }

    public string FilePath => @"C:\Sources\MediaOrcestrator\settings.toml";

    public string? GetStringValue(string key) => null;

    public void SetValue(string key, string value) { }

    public void Flush() { }
}

internal sealed class DesignDialogService : IDialogService
{
    public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);

    public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);

    public bool Confirm(string title, string message, bool defaultYes = false) => false;

    public bool ConfirmWarning(string title, string message, bool defaultYes = false) => false;

    public void Info(string title, string message) { }

    public void Warning(string title, string message) { }

    public void Error(string title, string message) { }
}

internal sealed class DesignAppLifetime : IAppLifetime
{
    public void Shutdown() { }
}

internal sealed class DesignClipboardService : IClipboardService
{
    public bool TrySetText(string? text) => true;
}

internal sealed class DesignShellLauncher : IShellLauncher
{
    public bool Open(string pathOrUrl) => true;

    public bool Reveal(string path) => true;

    public bool Start(string executable, params string[] arguments) => true;
}

internal sealed class DesignFilePicker : IFilePicker
{
    public string? PickFolder(string title, string? initialDirectory = null) => null;

    public string? PickFile(FileOpenRequest request) => null;

    public string? SaveFile(FileSaveRequest request) => null;
}

internal static class DesignUpdates
{
    public static AppUpdateViewModel ViewModel { get; } = new(
        new(new DesignReleaseProvider(), () => UpdatePreferences.FactoryRepo, "MediaOrcestrator-Wpf", NullLogger<AppUpdateManager>.Instance),
        new DesignDialogService(),
        new DesignSettingsStore(),
        NullLogger<AppUpdateViewModel>.Instance,
        new DesignShellLauncher());
}

internal sealed class DesignReleaseProvider : IReleaseProvider
{
    public Task<ReleaseInfo?> GetLatestReleaseAsync(string repo, string assetPattern, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ReleaseInfo?>(null);
    }

    public Task DownloadAssetAsync(
        string url,
        string targetPath,
        string? expectedDigest = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

internal static class DesignMcp
{
    public static McpServerOptions Options { get; } = new()
    {
        ServerName = "mediaorcestrator",
        AppName = "MediaOrcestrator",
        AppVersion = "0.0.0",
        ShotsDirectory = @"C:\Sources\MediaOrcestrator\shots",
        DefaultPort = 7655,
    };

    public static McpPreferences Preferences { get; } = new(new DesignSettingsStore(), Options);

    public static McpServerHost Host { get; } = new(
        Preferences,
        null!,
        null!,
        Options,
        new ServiceCollection().BuildServiceProvider());
}
