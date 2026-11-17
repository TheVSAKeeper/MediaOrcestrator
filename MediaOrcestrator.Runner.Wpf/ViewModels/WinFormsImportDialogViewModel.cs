using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using LiteDB;
using MediaOrcestrator.Domain.Migration;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class WinFormsImportDialogViewModel : ObservableObject, IDialogViewModel, ICancelableDialog
{
    private readonly WinFormsInstallationInspector _inspector;
    private readonly IWinFormsProcessDetector _processDetector;
    private readonly WinFormsImportService _importService;
    private readonly ISettingsStore _settingsStore;
    private readonly IFilePicker _filePicker;
    private readonly IShellLauncher _shellLauncher;
    private readonly IAppLifetime _lifetime;
    private readonly UpdatePreferences _updates;
    private readonly LiteDatabase _currentDatabase;
    private readonly ILogger<WinFormsImportDialogViewModel> _logger;
    private readonly Paths? _currentPaths;

    private CancellationTokenSource? _inspectCts;
    private CancellationTokenSource? _waitCts;
    private CancellationTokenSource? _importCts;
    private bool _isClosed;
    private bool _shortcutNameEditedByUser;
    private bool _suppressShortcutTracking;

    [ObservableProperty]
    private bool _isFirstRunPrompt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSourcePath))]
    private string _sourceFolderPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartImportCommand))]
    private bool _isSourceValid;

    [ObservableProperty]
    private string? _inspectionErrorMessage;

    [ObservableProperty]
    private string _detectedVersionText = "–";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPausedRunningNotice))]
    [NotifyCanExecuteChangedFor(nameof(StartImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartWaitingForExitCommand))]
    private bool _isSourceRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPausedRunningNotice))]
    [NotifyCanExecuteChangedFor(nameof(StartImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartWaitingForExitCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelWaitingCommand))]
    private bool _isWaitingForExit;

    [ObservableProperty]
    private string _runningPidsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    [NotifyPropertyChangedFor(nameof(SourcesCount))]
    [NotifyPropertyChangedFor(nameof(MediasCount))]
    [NotifyPropertyChangedFor(nameof(RelationsCount))]
    [NotifyPropertyChangedFor(nameof(CoverTemplatesCount))]
    [NotifyPropertyChangedFor(nameof(HasWalLog))]
    [NotifyPropertyChangedFor(nameof(WalLogNoticeText))]
    [NotifyPropertyChangedFor(nameof(HardDiskPaths))]
    [NotifyPropertyChangedFor(nameof(HasHardDiskPaths))]
    [NotifyPropertyChangedFor(nameof(Warnings))]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    private WinFormsDatabaseSummary? _summary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSeparateInstanceMode))]
    [NotifyPropertyChangedFor(nameof(IsReplaceCurrentMode))]
    [NotifyPropertyChangedFor(nameof(StartButtonText))]
    [NotifyCanExecuteChangedFor(nameof(StartImportCommand))]
    private WinFormsImportDestinationMode _destinationMode = WinFormsImportDestinationMode.SeparateInstance;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartImportCommand))]
    private string _targetFolderPath = string.Empty;

    [ObservableProperty]
    private bool _createShortcut = true;

    [ObservableProperty]
    private string _shortcutName = string.Empty;

    [ObservableProperty]
    private bool _launchAfterCreate;

    [ObservableProperty]
    private bool _enableAutoSync;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanEditInputs))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(BrowseSourceFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseTargetFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshInspectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(DismissFirstRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isImporting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditInputs))]
    [NotifyPropertyChangedFor(nameof(ShowDestinationOptions))]
    [NotifyCanExecuteChangedFor(nameof(BrowseSourceFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseTargetFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshInspectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartImportCommand))]
    private bool _isCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CreatedTargetFolder))]
    [NotifyPropertyChangedFor(nameof(CreatedShortcutPath))]
    [NotifyPropertyChangedFor(nameof(HasCreatedShortcut))]
    private WinFormsSeparateInstanceResult? _separateResult;

    [ObservableProperty]
    private string _progressStatusText = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string? _errorMessage;

    public WinFormsImportDialogViewModel(
        WinFormsInstallationInspector inspector,
        IWinFormsProcessDetector processDetector,
        WinFormsImportService importService,
        ISettingsStore settingsStore,
        IFilePicker filePicker,
        IShellLauncher shellLauncher,
        IAppLifetime lifetime,
        UpdatePreferences updates,
        LiteDatabase currentDatabase,
        ILogger<WinFormsImportDialogViewModel> logger,
        Paths? currentPaths = null)
    {
        _inspector = inspector;
        _processDetector = processDetector;
        _importService = importService;
        _settingsStore = settingsStore;
        _filePicker = filePicker;
        _shellLauncher = shellLauncher;
        _lifetime = lifetime;
        _updates = updates;
        _currentDatabase = currentDatabase;
        _logger = logger;
        _currentPaths = currentPaths;
    }

    public event EventHandler<bool>? RequestClose;

    public bool CanCancel => !IsImporting;

    public bool IsIdle => !IsImporting;

    public bool CanEditInputs => !IsImporting && !IsCompleted;

    public bool ShowDestinationOptions => !IsCompleted;

    public bool HasSourcePath => !string.IsNullOrWhiteSpace(SourceFolderPath);

    public bool HasSummary => Summary is not null;

    public bool ShowPausedRunningNotice => IsSourceRunning && !IsWaitingForExit;

    public int SourcesCount => Summary?.SourcesCount ?? 0;

    public int MediasCount => Summary?.MediasCount ?? 0;

    public int RelationsCount => Summary?.RelationsCount ?? 0;

    public int CoverTemplatesCount => Summary?.CoverTemplatesCount ?? 0;

    public bool HasWalLog => Summary?.HasWalLog ?? false;

    public string WalLogNoticeText => Summary is { HasWalLog: true }
        ? $"Найден журнал *-log.db ({FormatBytes(Summary.WalLogSizeBytes)}) – несохранённые записи попадут в снимок."
        : string.Empty;

    public IReadOnlyList<string> HardDiskPaths => Summary?.HardDiskPaths ?? [];

    public bool HasHardDiskPaths => HardDiskPaths.Count > 0;

    public IReadOnlyList<WinFormsImportWarning> Warnings => Summary is null
        ? []
        : Summary.Warnings.Where(w => w.Kind != WinFormsImportWarningKind.PendingWalLog).ToArray();

    public bool HasWarnings => Warnings.Count > 0;

    public bool IsSeparateInstanceMode
    {
        get => DestinationMode == WinFormsImportDestinationMode.SeparateInstance;
        set
        {
            if (value)
            {
                DestinationMode = WinFormsImportDestinationMode.SeparateInstance;
            }
        }
    }

    public bool IsReplaceCurrentMode
    {
        get => DestinationMode == WinFormsImportDestinationMode.ReplaceCurrent;
        set
        {
            if (value)
            {
                DestinationMode = WinFormsImportDestinationMode.ReplaceCurrent;
            }
        }
    }

    public string StartButtonText => IsReplaceCurrentMode
        ? "Заменить и перезапустить"
        : "Создать экземпляр";

    public string? CreatedTargetFolder => SeparateResult?.TargetFolder;

    public string? CreatedShortcutPath => SeparateResult?.ShortcutPath;

    public bool HasCreatedShortcut => !string.IsNullOrWhiteSpace(CreatedShortcutPath);

    public bool CanStartImport =>
        !IsImporting
        && !IsCompleted
        && IsSourceValid
        && !IsSourceRunning
        && !IsWaitingForExit
        && (IsReplaceCurrentMode || !string.IsNullOrWhiteSpace(TargetFolderPath));

    internal string CurrentAppBaseDirectory { get; set; } = AppContext.BaseDirectory;

    internal TimeSpan ProcessPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    internal WinFormsInspectionResult? LastInspection { get; private set; }

    internal Task? ActiveInspectionTask { get; private set; }

    internal Task? ActiveWaitTask { get; private set; }

    public void Configure(bool isFirstRunPrompt)
    {
        _isClosed = false;
        CancelInspectionCore();
        CancelWaitingCore();
        IsFirstRunPrompt = isFirstRunPrompt;
        IsCompleted = false;
        SeparateResult = null;
        ErrorMessage = null;
        ProgressStatusText = string.Empty;
        ProgressPercent = 0;
        DestinationMode = WinFormsImportDestinationMode.SeparateInstance;
        EnableAutoSync = false;
        CreateShortcut = true;
        LaunchAfterCreate = false;
    }

    partial void OnSourceFolderPathChanged(string value)
    {
        if (!IsCompleted)
        {
            ErrorMessage = null;
        }

        InspectSource();
    }

    partial void OnTargetFolderPathChanged(string value)
    {
        if (!IsCompleted)
        {
            ErrorMessage = null;
        }

        if (_shortcutNameEditedByUser)
        {
            return;
        }

        _suppressShortcutTracking = true;
        try
        {
            ShortcutName = SuggestShortcutName(value);
        }
        finally
        {
            _suppressShortcutTracking = false;
        }
    }

    partial void OnShortcutNameChanged(string value)
    {
        if (!_suppressShortcutTracking)
        {
            _shortcutNameEditedByUser = !string.IsNullOrWhiteSpace(value);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditInputs))]
    private void BrowseSourceFolder()
    {
        var initial = Directory.Exists(SourceFolderPath) ? SourceFolderPath : null;
        var selected = _filePicker.PickFolder("Выберите папку с установленным WinForms", initial);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SourceFolderPath = selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditInputs))]
    private void BrowseTargetFolder()
    {
        var initial = Directory.Exists(TargetFolderPath) ? TargetFolderPath : null;
        var selected = _filePicker.PickFolder("Выберите пустую папку для нового экземпляра WPF", initial);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            TargetFolderPath = selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditInputs))]
    private void RefreshInspection() => InspectSource();

    private static string SuggestShortcutName(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return string.Empty;
        }

        try
        {
            var trimmed = targetPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(folderName)
                ? "MediaOrcestrator"
                : $"MediaOrcestrator – {folderName}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            <= 0 => "0 Б",
            < 1024 => $"{bytes} Б",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} КБ",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} МБ",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} ГБ",
        };
    }
}
