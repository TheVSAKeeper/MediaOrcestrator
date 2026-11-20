using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MaintenanceViewModel : ObservableObject
{
    private const string Key_TempFiles = "temp_files";
    private const string Key_OldLogs = "old_logs";
    private const string Key_OldBackups = "old_backups";

    private const int DefaultLogRetentionDays = 30;
    private const int DefaultBackupRetentionDays = 14;
    private const int DefaultTempRetentionDays = 0;

    private readonly DatabaseMaintenanceService _maintenance;
    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MaintenanceViewModel> _logger;
    private readonly string _tempPath;
    private readonly string _logsPath;
    private readonly string _backupsPath;
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

    public MaintenanceViewModel(
        DatabaseMaintenanceService maintenance,
        ActionHolder actionHolder,
        IDialogService dialogs,
        ILogger<MaintenanceViewModel> logger)
    {
        _maintenance = maintenance;
        _actionHolder = actionHolder;
        _dialogs = dialogs;
        _logger = logger;

        _tempPath = _maintenance.TempPath;
        _logsPath = _maintenance.LogsPath;
        _backupsPath = _maintenance.BackupsPath;

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
    }

    internal event EventHandler? CleanupFinished;

    public ObservableCollection<MaintenanceItemViewModel> SafeItems { get; }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAllAsync() => IsBusy ? Task.CompletedTask : LoadSizesAsync();

    internal async Task LoadSizesAsync()
    {
        var sizes = await Task.Run(() => SafeItems.Select(MeasureItem).ToArray());
        for (var i = 0; i < sizes.Length; i++)
        {
            SafeItems[i].SizeText = sizes[i];
        }
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
                catch (OperationCanceledException) when (token.IsCancellationRequested)
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
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
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
            CleanupFinished?.Invoke(this, EventArgs.Empty);
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
            Key_OldLogs => FormatBytes(_maintenance.MeasureOldFiles(_logsPath, ToAge(item.AgeDays), "*.log", "*.txt")),
            Key_OldBackups => FormatBytes(_maintenance.MeasureOldFiles(_backupsPath, ToAge(item.AgeDays), "*.db")),
            _ => "–",
        };
    }

    private long MeasureTempFiles(int ageDays)
    {
        if (ageDays <= 0)
        {
            return _maintenance.MeasureFolder(_tempPath, recursive: true);
        }

        return _maintenance.MeasureOldFiles(_tempPath, ToAge(ageDays), "*");
    }

    private string CleanupItem(MaintenanceItemViewModel item)
    {
        return item.Key switch
        {
            Key_TempFiles => DescribeFolderClean(
                _maintenance.CleanFolder(_tempPath, "*", ToAge(item.AgeDays), recursive: true)),
            Key_OldLogs => DescribeFolderClean(SumFolderResults(
                _maintenance.CleanFolder(_logsPath, "*.log", ToAge(item.AgeDays)),
                _maintenance.CleanFolder(_logsPath, "*.txt", ToAge(item.AgeDays)))),
            Key_OldBackups => DescribeFolderClean(
                _maintenance.CleanFolder(_backupsPath, "*.db", ToAge(item.AgeDays))),
            _ => "Готово",
        };
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
}
