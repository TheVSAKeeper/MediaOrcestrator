using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MaintenanceViewModel : ObservableObject
{
    private const string Key_TempFiles = "temp_files";
    private const string Key_OldLogs = "old_logs";
    private const string Key_OldBackups = "old_backups";

    private const int DefaultLogRetentionDays = 30;
    private const int DefaultBackupRetentionDays = 14;
    private const int DefaultTempRetentionDays = 0;

    private static readonly DateTime ProcessStarted = Process.GetCurrentProcess().StartTime;

    private readonly DatabaseMaintenanceService _maintenance;
    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MaintenanceViewModel> _logger;
    private readonly IShellLauncher _shellLauncher;
    private readonly IClipboardService _clipboard;
    private CancellationTokenSource? _runCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSafeCommand))]
    private int _safeSelectedCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSafeCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMax = 1;

    [ObservableProperty]
    private string _databaseSizeText = "–";

    [ObservableProperty]
    private string _upTimeText = "–";

    public MaintenanceViewModel(
        DatabaseMaintenanceService maintenance,
        ActionHolder actionHolder,
        IDialogService dialogs,
        ILogger<MaintenanceViewModel> logger,
        IShellLauncher shellLauncher,
        IClipboardService clipboard)
    {
        _maintenance = maintenance;
        _actionHolder = actionHolder;
        _dialogs = dialogs;
        _logger = logger;
        _shellLauncher = shellLauncher;
        _clipboard = clipboard;

        AppVersion = ResolveAppVersion();
        AppDirectory = AppContext.BaseDirectory;
        RuntimeDescription = RuntimeInformation.FrameworkDescription;
        OperatingSystem = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
        MachineName = Environment.MachineName;
        ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
        ProcessId = Environment.ProcessId;
        DatabasePath = _maintenance.DatabasePath;
        TempPath = _maintenance.TempPath;
        LogsPath = _maintenance.LogsPath;
        BackupsPath = _maintenance.BackupsPath;

        SafeItems = new ObservableCollection<MaintenanceItemViewModel>
        {
            new()
            {
                Key = Key_TempFiles,
                Title = "Временные файлы",
                Description = "Промежуточные файлы загрузок и публикаций в папке temp/",
                HasAgeFilter = true,
                MinAgeDays = 0,
                MaxAgeDays = 90,
                AgeDays = DefaultTempRetentionDays,
            },
            new()
            {
                Key = Key_OldLogs,
                Title = "Старые логи",
                Description = "Файлы logs/wpf-*.log и logs/log-*.txt",
                HasAgeFilter = true,
                MinAgeDays = 0,
                MaxAgeDays = 365,
                AgeDays = DefaultLogRetentionDays,
            },
            new()
            {
                Key = Key_OldBackups,
                Title = "Старые бэкапы БД",
                Description = "Файлы backups/*.db",
                HasAgeFilter = true,
                MinAgeDays = 0,
                MaxAgeDays = 365,
                AgeDays = DefaultBackupRetentionDays,
            },
        };

        foreach (var item in SafeItems)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }

        _ = RefreshAllAsync();
    }

    public ObservableCollection<MaintenanceItemViewModel> SafeItems { get; }

    public string AppVersion { get; }

    public string AppDirectory { get; }

    public string RuntimeDescription { get; }

    public string OperatingSystem { get; }

    public string MachineName { get; }

    public string ProcessArchitecture { get; }

    public int ProcessId { get; }

    public string DatabasePath { get; }

    public string TempPath { get; }

    public string LogsPath { get; }

    public string BackupsPath { get; }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAllAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await Task.Run(() =>
        {
            foreach (var item in SafeItems)
            {
                item.SizeText = MeasureItem(item);
            }

            DatabaseSizeText = FormatBytes(_maintenance.MeasureDatabaseFile());
        });

        UpTimeText = FormatUpTime(DateTime.Now - ProcessStarted);
    }

    [RelayCommand(CanExecute = nameof(CanRunSafe))]
    private async Task RunSafeAsync()
    {
        var selected = SafeItems.Where(i => i.IsSelected).ToList();

        if (selected.Count == 0)
        {
            return;
        }

        if (!_dialogs.ConfirmWarning(
                "Освободить место?",
                $"Будут удалены файлы по {selected.Count} категориям. Перед очисткой будет создан ручной бэкап БД. Продолжить?"))
        {
            return;
        }

        _runCts?.Dispose();
        _runCts = new();
        var token = _runCts.Token;

        IsBusy = true;
        StatusText = "Подготовка";
        ProgressMax = selected.Count + 1;
        ProgressValue = 0;

        var running = _actionHolder.Register(
            "Очистка файлов",
            $"0/{selected.Count}",
            selected.Count,
            _runCts,
            kind: ActionKind.Other);

        try
        {
            using var scope = _actionHolder.BeginScope(running);

            StatusText = "Создание ручного бэкапа БД";
            running.Status = "Резервная копия";

            await Task.Run(() => _maintenance.RunPreCleanupBackup(), token);

            ProgressValue = 1;
            running.SetProgress(1);

            var processed = 0;

            foreach (var item in selected)
            {
                token.ThrowIfCancellationRequested();

                processed++;
                item.State = MaintenanceItemState.Running;
                item.ResultText = "Очистка…";
                StatusText = $"Очистка: {item.Title}";
                running.Status = $"{processed}/{selected.Count} – {item.Title}";

                try
                {
                    var summary = await Task.Run(() => CleanupItem(item), token);
                    item.State = MaintenanceItemState.Success;
                    item.ResultText = summary;
                    item.IsSelected = false;
                    item.SizeText = MeasureItem(item);
                }
                catch (OperationCanceledException)
                {
                    item.State = MaintenanceItemState.Idle;
                    item.ResultText = null;
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при очистке {Key}", item.Key);
                    item.State = MaintenanceItemState.Error;
                    item.ResultText = "Ошибка: " + ex.Message;
                }

                ProgressValue = processed + 1;
                running.SetProgress(processed + 1);
            }

            StatusText = "Готово";
            running.Finish($"Очищено: {processed}");
            DatabaseSizeText = FormatBytes(_maintenance.MeasureDatabaseFile());
        }
        catch (OperationCanceledException)
        {
            StatusText = "Отменено";
            running.MarkCancelled("Отменено пользователем");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Сбой при очистке");
            StatusText = "Ошибка: " + ex.Message;
            running.Fail(ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            ProgressValue = ProgressMax;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        try
        {
            _runCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    [RelayCommand]
    private void SelectAllSafe() => SetSelection(SafeItems, true);

    [RelayCommand]
    private void SelectNoneSafe() => SetSelection(SafeItems, false);

    private static void SetSelection(IEnumerable<MaintenanceItemViewModel> items, bool value)
    {
        foreach (var item in items)
        {
            item.IsSelected = value;
        }
    }

    [RelayCommand]
    private void OpenAppFolder() => RevealInExplorer(AppDirectory);

    [RelayCommand]
    private void OpenLogsFolder() => RevealInExplorer(LogsPath);

    [RelayCommand]
    private void OpenBackupsFolder() => RevealInExplorer(BackupsPath);

    [RelayCommand]
    private void OpenTempFolder() => RevealInExplorer(TempPath);

    [RelayCommand]
    private void OpenDatabaseFolder()
    {
        if (File.Exists(DatabasePath))
        {
            SelectInExplorer(DatabasePath);
        }
        else
        {
            RevealInExplorer(Path.GetDirectoryName(DatabasePath));
        }
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        var report = BuildDiagnosticsReport();

        if (_clipboard.TrySetText(report))
        {
            StatusText = "Сводка скопирована в буфер";
        }
        else
        {
            StatusText = "Не удалось скопировать сводку в буфер";
            _logger.LogWarning("Не удалось скопировать сводку в буфер");
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MaintenanceItemViewModel item)
        {
            return;
        }

        if (e.PropertyName == nameof(MaintenanceItemViewModel.IsSelected))
        {
            SafeSelectedCount = SafeItems.Count(i => i.IsSelected);
            return;
        }

        if (e.PropertyName == nameof(MaintenanceItemViewModel.AgeDays))
        {
            _ = RefreshItemAsync(item);
        }
    }

    private async Task RefreshItemAsync(MaintenanceItemViewModel item)
    {
        if (IsBusy)
        {
            return;
        }

        item.SizeText = await Task.Run(() => MeasureItem(item));
    }

    private bool CanRunSafe() => !IsBusy && SafeSelectedCount > 0;

    private bool CanRefresh() => !IsBusy;

    private string MeasureItem(MaintenanceItemViewModel item)
    {
        return item.Key switch
        {
            Key_TempFiles => FormatBytes(MeasureTempFiles(item.AgeDays)),
            Key_OldLogs => FormatBytes(_maintenance.MeasureOldFiles(LogsPath, ToAge(item.AgeDays), "*.log", "*.txt")),
            Key_OldBackups => FormatBytes(_maintenance.MeasureOldFiles(BackupsPath, ToAge(item.AgeDays), "*.db")),
            _ => "–",
        };
    }

    private long MeasureTempFiles(int ageDays)
    {
        if (ageDays <= 0)
        {
            return _maintenance.MeasureFolder(TempPath, recursive: true);
        }

        return _maintenance.MeasureOldFiles(TempPath, ToAge(ageDays), "*");
    }

    private string CleanupItem(MaintenanceItemViewModel item)
    {
        return item.Key switch
        {
            Key_TempFiles => DescribeFolderClean(
                _maintenance.CleanFolder(TempPath, "*", ToAge(item.AgeDays), recursive: true)),
            Key_OldLogs => DescribeFolderClean(SumFolderResults(
                _maintenance.CleanFolder(LogsPath, "*.log", ToAge(item.AgeDays)),
                _maintenance.CleanFolder(LogsPath, "*.txt", ToAge(item.AgeDays)))),
            Key_OldBackups => DescribeFolderClean(
                _maintenance.CleanFolder(BackupsPath, "*.db", ToAge(item.AgeDays))),
            _ => "Готово",
        };
    }

    private string BuildDiagnosticsReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("MediaOrcestrator – сводка диагностики");
        sb.AppendLine($"Дата:      {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("Приложение");
        sb.AppendLine($"  Версия:    {AppVersion}");
        sb.AppendLine($"  Папка:     {AppDirectory}");
        sb.AppendLine($"  Runtime:   {RuntimeDescription}");
        sb.AppendLine($"  Arch:      {ProcessArchitecture}");
        sb.AppendLine($"  PID:       {ProcessId}");
        sb.AppendLine($"  Старт:     {ProcessStarted:yyyy-MM-dd HH:mm:ss} (uptime {UpTimeText})");
        sb.AppendLine();
        sb.AppendLine("Система");
        sb.AppendLine($"  OS:        {OperatingSystem}");
        sb.AppendLine($"  Машина:    {MachineName}");
        sb.AppendLine();
        sb.AppendLine("Папки");
        sb.AppendLine($"  database:  {DatabasePath} ({DatabaseSizeText})");
        sb.AppendLine($"  temp:      {TempPath}");
        sb.AppendLine($"  logs:      {LogsPath}");
        sb.AppendLine($"  backups:   {BackupsPath}");
        sb.AppendLine();
        sb.AppendLine("Размеры файловой очистки (на момент копирования)");

        foreach (var item in SafeItems)
        {
            sb.AppendLine($"  {item.Title,-32} {item.SizeText}");
        }

        return sb.ToString();
    }

    private static TimeSpan? ToAge(int days) => days <= 0 ? null : TimeSpan.FromDays(days);

    private static (int Deleted, long FreedBytes) SumFolderResults(
        (int Deleted, long FreedBytes) a,
        (int Deleted, long FreedBytes) b)
    {
        return (a.Deleted + b.Deleted, a.FreedBytes + b.FreedBytes);
    }

    private static string DescribeFolderClean((int Deleted, long FreedBytes) result)
    {
        return $"Удалено файлов: {result.Deleted} ({FormatBytes(result.FreedBytes)})";
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            <= 0 => "пусто",
            < 1024 => $"{bytes} Б",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} КБ",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} МБ",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} ГБ",
        };
    }

    private static string FormatUpTime(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays} д {span.Hours} ч {span.Minutes} мин";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours} ч {span.Minutes} мин";
        }

        return $"{(int)span.TotalMinutes} мин {span.Seconds} с";
    }

    private static string ResolveAppVersion()
    {
        var assembly = typeof(MaintenanceViewModel).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "–";
    }

    private void RevealInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось открыть папку {Path}", path);
            return;
        }

        if (!_shellLauncher.Open(path))
        {
            _logger.LogWarning("Не удалось открыть папку {Path}", path);
        }
    }

    private void SelectInExplorer(string filePath)
    {
        if (!_shellLauncher.Reveal(filePath))
        {
            _logger.LogWarning("Не удалось показать файл в проводнике: {Path}", filePath);
        }
    }
}
