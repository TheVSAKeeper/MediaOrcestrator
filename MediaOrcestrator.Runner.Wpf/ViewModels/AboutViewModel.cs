using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly ErrorReportService _errorReports;
    private readonly UpdatePreferences _updates;
    private readonly ILogger<AboutViewModel> _logger;
    private readonly IShellLauncher _shellLauncher;
    private readonly IClipboardService _clipboard;

    public AboutViewModel(
        ErrorReportService errorReports,
        UpdatePreferences updates,
        ILogger<AboutViewModel> logger,
        IShellLauncher shellLauncher,
        IClipboardService clipboard)
    {
        _errorReports = errorReports;
        _updates = updates;
        _logger = logger;
        _shellLauncher = shellLauncher;
        _clipboard = clipboard;

        _updates.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(RepositoryHandle));
        };
    }

    public string AppName => "MediaOrcestrator";

    public string Version => ResolveAppVersion();

    public string Tagline => "Дирижёр медиа-зоопарка: YouTube, RuTube, VK, Telegram и локальный диск – за одним пультом";

    public string Description =>
        "MediaOrcestrator держит весь контент под одной палочкой: тянет ролики и метаданные с YouTube, " +
        "RuTube, VK Video, Telegram и локального диска, сводит их в единый каталог, ловит дубликаты, " +
        "переносит между площадками и публикует. Связи серий и эпизодов, обложки, комментарии и досинк " +
        "на новые медиа – оркестр играет сам, тебе остаётся дирижировать.";

    public string DotNetVersion => RuntimeInformation.FrameworkDescription;

    public string OperatingSystem => RuntimeInformation.OSDescription;

    public string Architecture => RuntimeInformation.ProcessArchitecture.ToString();

    public string RepositoryHandle => _updates.Repo;

    private string RepositoryUrl => $"https://github.com/{_updates.Repo}";

    private string ReleasesUrl => RepositoryUrl + "/releases";

    [RelayCommand]
    private void OpenRepository() => OpenUrl(RepositoryUrl);

    [RelayCommand]
    private void OpenReleases() => OpenUrl(ReleasesUrl);

    [RelayCommand]
    private void ReportIssue()
    {
        try
        {
            var payload = _errorReports.Build(null, userContext: "Отчёт из раздела «О программе»");

            if (!_clipboard.TrySetText(payload.LogClipboard))
            {
                _logger.LogWarning("Не удалось положить лог-секцию в буфер обмена");
            }

            if (!_shellLauncher.Open(payload.IssueUrl))
            {
                _logger.LogError("Не удалось открыть форму репорта");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось открыть форму репорта");
        }
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        var text = new StringBuilder()
            .AppendLine($"{AppName} {Version}")
            .AppendLine($".NET: {DotNetVersion}")
            .AppendLine($"ОС: {OperatingSystem}")
            .Append($"Архитектура: {Architecture}")
            .ToString();

        if (!_clipboard.TrySetText(text))
        {
            _logger.LogWarning("Не удалось скопировать сведения об окружении в буфер обмена");
        }
    }

    private void OpenUrl(string url)
    {
        if (!_shellLauncher.Open(url))
        {
            _logger.LogWarning("Не удалось открыть ссылку {Url}", url);
        }
    }

    private static string ResolveAppVersion()
    {
        var assembly = typeof(AboutViewModel).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "–";
    }
}
