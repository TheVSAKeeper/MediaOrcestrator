using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public enum ToolState
{
    None = 0,
    Unknown = 1,
    NotInstalled = 2,
    UpdateAvailable = 3,
    UpToDate = 4,
}

public sealed partial class ToolItemViewModel : ObservableObject
{
    private readonly IShellLauncher _shellLauncher;
    private readonly IClipboardService _clipboard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    [NotifyPropertyChangedFor(nameof(HasAction))]
    [NotifyPropertyChangedFor(nameof(VersionSummary))]
    private ToolState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionSummary))]
    private string _installedVersion = "–";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionSummary))]
    private string _latestVersion = "–";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    private string? _resolvedPath;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    public ToolItemViewModel(string name, string? resolvedPath, IShellLauncher shellLauncher, IClipboardService clipboard)
    {
        _shellLauncher = shellLauncher;
        _clipboard = clipboard;
        Name = name;
        ResolvedPath = resolvedPath;

        if (resolvedPath is null)
        {
            State = ToolState.NotInstalled;
        }
        else
        {
            InstalledVersion = "…";
            State = ToolState.Unknown;
        }
    }

    public string Name { get; }

    public string StatusText => State switch
    {
        ToolState.NotInstalled => "Не установлен",
        ToolState.UpdateAvailable => "Есть обновление",
        ToolState.UpToDate => "Актуально",
        _ => "Ожидание проверки",
    };

    public string ActionText => State switch
    {
        ToolState.NotInstalled => "Установить",
        ToolState.UpdateAvailable => "Обновить",
        ToolState.UpToDate => string.Empty,
        _ => "Проверить",
    };

    public bool HasAction => !string.IsNullOrEmpty(ActionText);

    public string VersionSummary => State switch
    {
        ToolState.NotInstalled => $"Доступно {LatestVersion}",
        ToolState.UpdateAvailable => $"{InstalledVersion} → {LatestVersion}",
        ToolState.UpToDate => InstalledVersion,
        _ => InstalledVersion,
    };

    private bool HasResolvedPath => !string.IsNullOrWhiteSpace(ResolvedPath);

    [RelayCommand(CanExecute = nameof(HasResolvedPath))]
    private void OpenFolder()
    {
        if (!File.Exists(ResolvedPath))
        {
            return;
        }

        _shellLauncher.Reveal(ResolvedPath!);
    }

    [RelayCommand(CanExecute = nameof(HasResolvedPath))]
    private void CopyPath()
    {
        _clipboard.TrySetText(ResolvedPath!);
    }
}
