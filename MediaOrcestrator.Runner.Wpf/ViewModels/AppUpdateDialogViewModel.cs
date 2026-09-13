using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class AppUpdateDialogViewModel : ObservableObject, IDialogViewModel
{
    private readonly AppUpdateManager _updateManager;
    private readonly ILogger _logger;
    private readonly IShellLauncher _shellLauncher;
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaterCommand))]
    [NotifyCanExecuteChangedFor(nameof(SkipCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isDownloading;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string? _errorMessage;

    public AppUpdateDialogViewModel(
        AppUpdateManager updateManager,
        AppUpdateInfo update,
        ILogger logger,
        IShellLauncher shellLauncher)
    {
        _updateManager = updateManager;
        Update = update;
        _logger = logger;
        _shellLauncher = shellLauncher;
    }

    public event EventHandler<bool>? RequestClose;

    public AppUpdateInfo Update { get; }

    public string CurrentVersion => _updateManager.CurrentVersion.ToString();

    public string NewVersion => Update.Version;

    public string SizeText => FormatSize(Update.Size);

    public string ReleaseNotes => StripWhatsChangedHeading(Update.ReleaseNotes);

    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);

    public bool SkipRequested { get; private set; }

    public bool IsIdle => !IsDownloading;

    public string? ReleasePageUrl => BuildReleasePageUrl(Update.DownloadUrl);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task InstallAsync()
    {
        _downloadCts?.Dispose();
        _downloadCts = new();

        IsDownloading = true;
        ErrorMessage = null;
        StatusText = "Скачивание обновления...";
        ProgressValue = 0;

        var progress = new Progress<double>(value =>
        {
            ProgressValue = Math.Clamp(value * 100, 0, 100);
            StatusText = $"Скачивание обновления... {value:P0}";
        });

        try
        {
            var zipPath = await Task.Run(() =>
                _updateManager.DownloadUpdateAsync(Update, progress, _downloadCts.Token));

            StatusText = "Обновление скачано. Запуск Updater...";
            _logger.LogInformation("Обновление {Version} готово к установке: {Path}", Update.Version, zipPath);

            _updateManager.ApplyUpdate(zipPath);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Скачивание обновления {Version} отменено пользователем", Update.Version);
            StatusText = "Отменено";
            IsDownloading = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось скачать обновление {Version}", Update.Version);
            ErrorMessage = ex.Message;
            StatusText = "Ошибка";
            IsDownloading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsDownloading))]
    private void Cancel()
    {
        try
        {
            _downloadCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Later()
    {
        RequestClose?.Invoke(this, false);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Skip()
    {
        SkipRequested = true;
        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (string.IsNullOrWhiteSpace(ReleasePageUrl))
        {
            return;
        }

        if (!_shellLauncher.Open(ReleasePageUrl))
        {
            _logger.LogWarning("Не удалось открыть страницу релиза {Url}", ReleasePageUrl);
            ErrorMessage = "Не удалось открыть страницу релиза в браузере";
        }
    }

    private static string? BuildReleasePageUrl(string downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        const string marker = "/releases/download/";
        var idx = downloadUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (idx < 0)
        {
            return null;
        }

        var afterMarker = downloadUrl[(idx + marker.Length)..];
        var slash = afterMarker.IndexOf('/');

        if (slash <= 0)
        {
            return null;
        }

        var tag = afterMarker[..slash];
        return downloadUrl[..idx] + "/releases/tag/" + tag;
    }

    private static string StripWhatsChangedHeading(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        var lines = markdown.Split('\n');
        var start = 0;

        while (start < lines.Length && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        if (start < lines.Length
            && lines[start].TrimEnd('\r').Trim().Equals("## What's Changed", StringComparison.OrdinalIgnoreCase))
        {
            start++;
        }

        return string.Join('\n', lines, start, lines.Length - start).Trim();
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} Б",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} КБ",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} МБ",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} ГБ",
        };
    }
}
