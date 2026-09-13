using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class DatabaseCleanupViewModel : ObservableObject
{
    private const string Key_Comments = "media_comments";
    private const string Key_Medias = "medias";
    private const string Key_Sources = "sources";
    private const string Key_Relations = "source_relations";

    private readonly DatabaseMaintenanceService _maintenance;
    private readonly DatabaseBackupService _backups;
    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly IAppLifetime _lifetime;
    private readonly ILogger<DatabaseCleanupViewModel> _logger;
    private readonly IShellLauncher _shellLauncher;
    private CancellationTokenSource? _runCts;
    private List<BackupItemViewModel> _allBackups = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunDangerCommand))]
    private int _dangerSelectedCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunDangerCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateBackupNowCommand))]
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
    private int _backupsCount;

    [ObservableProperty]
    private string _backupsTotalSizeText = "–";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleExpandLabel))]
    [NotifyPropertyChangedFor(nameof(ShowToggleExpand))]
    private bool _isBackupsExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleExpandLabel))]
    [NotifyPropertyChangedFor(nameof(ShowToggleExpand))]
    private int _hiddenBackupsCount;

    public bool ShowToggleExpand => IsBackupsExpanded || HiddenBackupsCount > 0;

    [ObservableProperty]
    private string? _pendingRestoreFileName;

    public const int CollapsedBackupsLimit = 10;

    public DatabaseCleanupViewModel(
        DatabaseMaintenanceService maintenance,
        DatabaseBackupService backups,
        ActionHolder actionHolder,
        IDialogService dialogs,
        IAppLifetime lifetime,
        ILogger<DatabaseCleanupViewModel> logger,
        IShellLauncher shellLauncher)
    {
        _maintenance = maintenance;
        _backups = backups;
        _actionHolder = actionHolder;
        _dialogs = dialogs;
        _lifetime = lifetime;
        _logger = logger;
        _shellLauncher = shellLauncher;

        DatabasePath = _maintenance.DatabasePath;
        BackupsPath = _maintenance.BackupsPath;

        DangerItems = new ObservableCollection<MaintenanceItemViewModel>
        {
            new() { Key = Key_Comments, IsDangerous = true, Title = "Комментарии (media_comments)", Description = "Все сохранённые комментарии к медиа со всех площадок" },
            new() { Key = Key_Medias, IsDangerous = true, Title = "Медиа (medias)", Description = "Записи о медиа и их привязки к источникам – переразведку придётся делать заново" },
            new() { Key = Key_Sources, IsDangerous = true, Title = "Источники (sources)", Description = "Список аккаунтов/папок. После очистки нужно будет создать их заново" },
            new() { Key = Key_Relations, IsDangerous = true, Title = "Связи источников (source_relations)", Description = "Направления синка между источниками" },
        };

        foreach (var item in DangerItems)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }

        Backups = new ObservableCollection<BackupItemViewModel>();

        _ = RefreshAsync();
    }

    public ObservableCollection<MaintenanceItemViewModel> DangerItems { get; }

    public ObservableCollection<BackupItemViewModel> Backups { get; }

    public string DatabasePath { get; }

    public string BackupsPath { get; }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await Task.Run(() =>
        {
            foreach (var item in DangerItems)
            {
                item.SizeText = FormatCount(_maintenance.CountCollection(item.Key));
            }

            DatabaseSizeText = FormatBytes(_maintenance.MeasureDatabaseFile());
        });

        await ReloadBackupsListAsync();
    }

    private async Task ReloadBackupsListAsync()
    {
        var entries = await Task.Run(_backups.GetBackups);

        var all = new List<BackupItemViewModel>(entries.Count);
        long total = 0;

        foreach (var entry in entries)
        {
            all.Add(new()
            {
                FileName = entry.FileName,
                CreatedAt = entry.CreatedAt,
                Trigger = entry.Trigger,
                SizeBytes = entry.SizeBytes,
            });
            total += entry.SizeBytes;
        }

        _allBackups = all;
        BackupsCount = all.Count;
        BackupsTotalSizeText = FormatBytes(total);

        ApplyVisibleBackups();
        PendingRestoreFileName = _backups.GetPendingRestore();
    }

    private void ApplyVisibleBackups()
    {
        var total = _allBackups.Count;
        var limit = IsBackupsExpanded ? total : Math.Min(total, CollapsedBackupsLimit);

        Backups.Clear();

        for (var i = 0; i < limit; i++)
        {
            Backups.Add(_allBackups[i]);
        }

        HiddenBackupsCount = Math.Max(0, total - limit);
    }

    public string ToggleExpandLabel => IsBackupsExpanded
        ? "Свернуть"
        : HiddenBackupsCount > 0
            ? $"Показать ещё {HiddenBackupsCount}"
            : "Развернуть";

    partial void OnIsBackupsExpandedChanged(bool value)
    {
        ApplyVisibleBackups();
    }

    [RelayCommand]
    private void ToggleBackupsExpanded()
    {
        IsBackupsExpanded = !IsBackupsExpanded;
    }

    [RelayCommand(CanExecute = nameof(CanRunDanger))]
    private async Task RunDangerAsync()
    {
        var selected = DangerItems.Where(i => i.IsSelected).ToList();

        if (selected.Count == 0)
        {
            return;
        }

        var titles = string.Join(", ", selected.Select(i => i.Title));

        if (!_dialogs.ConfirmWarning(
                "Удалить записи из БД?",
                $"Будут необратимо удалены данные: {titles}.\n\n" +
                "Восстановить их можно будет только из бэкапа БД. Перед удалением будет создан ручной бэкап. Продолжить?"))
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
            "Удаление записей БД",
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
                item.ResultText = "Удаление…";
                StatusText = $"Удаление: {item.Title}";
                running.Status = $"{processed}/{selected.Count} – {item.Title}";

                try
                {
                    var removed = await Task.Run(() => _maintenance.ClearCollection(item.Key), token);
                    item.State = MaintenanceItemState.Success;
                    item.ResultText = $"Удалено записей: {removed}";
                    item.IsSelected = false;
                    item.SizeText = FormatCount(_maintenance.CountCollection(item.Key));
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
            await ReloadBackupsListAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Отменено";
            running.MarkCancelled("Отменено пользователем");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Сбой при очистке БД");
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
    private void SelectAllDanger() => SetSelection(DangerItems, true);

    [RelayCommand]
    private void SelectNoneDanger() => SetSelection(DangerItems, false);

    private static void SetSelection(IEnumerable<MaintenanceItemViewModel> items, bool value)
    {
        foreach (var item in items)
        {
            item.IsSelected = value;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateBackup))]
    private async Task CreateBackupNowAsync()
    {
        IsBusy = true;
        StatusText = "Создание ручного бэкапа БД";

        try
        {
            await Task.Run(() => _backups.Backup(BackupTrigger.Manual));
            DatabaseSizeText = FormatBytes(_maintenance.MeasureDatabaseFile());
            await ReloadBackupsListAsync();
            StatusText = "Бэкап создан";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось создать ручной бэкап");
            StatusText = "Ошибка бэкапа: " + ex.Message;
            _dialogs.Error("Бэкап не создан", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteBackupAsync(BackupItemViewModel? backup)
    {
        if (backup is null || backup.IsDeleting)
        {
            return;
        }

        if (!_dialogs.ConfirmWarning(
                "Удалить бэкап?",
                $"Файл «{backup.FileName}» ({backup.SizeText}, создан {backup.CreatedAtText}) будет удалён без возможности восстановления. Продолжить?"))
        {
            return;
        }

        backup.IsDeleting = true;

        try
        {
            await Task.Run(() => _backups.DeleteBackup(backup.FileName));
            await ReloadBackupsListAsync();
            StatusText = $"Удалён бэкап «{backup.FileName}»";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось удалить бэкап {FileName}", backup.FileName);
            _dialogs.Error("Бэкап не удалён", ex.Message);
        }
        finally
        {
            backup.IsDeleting = false;
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync(BackupItemViewModel? backup)
    {
        if (backup is null)
        {
            return;
        }

        if (!_dialogs.ConfirmWarning(
                "Восстановить базу из этого бэкапа?",
                $"Текущая БД будет сохранена под именем «{Path.GetFileNameWithoutExtension(DatabasePath)}_<timestamp>_pre-restore.db» в папке backups/, " +
                $"а затем заменена содержимым «{backup.FileName}» (создан {backup.CreatedAtText}, {backup.SizeText}).\n\n" +
                "Чтобы изменения применились, приложение нужно закрыть и открыть заново. " +
                "После перезапуска вы окажетесь на восстановленной БД.\n\nПодготовить восстановление?"))
        {
            return;
        }

        try
        {
            await Task.Run(() => _backups.PrepareRestore(backup.FileName));
            PendingRestoreFileName = _backups.GetPendingRestore();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось подготовить восстановление из {FileName}", backup.FileName);
            _dialogs.Error("Не удалось подготовить восстановление", ex.Message);
            return;
        }

        var closeNow = _dialogs.ConfirmWarning(
            "Закрыть приложение сейчас?",
            $"Восстановление подготовлено: при следующем старте БД будет заменена на «{backup.FileName}». " +
            "Закрыть приложение сейчас, чтобы вы могли тут же запустить его снова?",
            defaultYes: true);

        if (closeNow)
        {
            _lifetime.Shutdown();
        }
    }

    [RelayCommand]
    private void ClearPendingRestore()
    {
        try
        {
            _backups.CancelPendingRestore();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось снять запрос на восстановление БД");
        }
        finally
        {
            PendingRestoreFileName = _backups.GetPendingRestore();
        }
    }

    [RelayCommand]
    private void ShowBackupInExplorer(BackupItemViewModel? backup)
    {
        if (backup is null)
        {
            return;
        }

        var filePath = Path.Combine(BackupsPath, backup.FileName);

        if (!File.Exists(filePath))
        {
            _dialogs.Warning("Файл не найден", "Бэкап уже отсутствует на диске. Список будет обновлён.");
            _ = ReloadBackupsListAsync();
            return;
        }

        if (!_shellLauncher.Reveal(filePath))
        {
            _logger.LogWarning("Не удалось показать бэкап в проводнике: {Path}", filePath);
        }
    }

    [RelayCommand]
    private void OpenBackupsFolder()
    {
        try
        {
            Directory.CreateDirectory(BackupsPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось открыть папку бэкапов: {Path}", BackupsPath);
            return;
        }

        if (!_shellLauncher.Open(BackupsPath))
        {
            _logger.LogWarning("Не удалось открыть папку бэкапов: {Path}", BackupsPath);
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MaintenanceItemViewModel)
        {
            return;
        }

        if (e.PropertyName == nameof(MaintenanceItemViewModel.IsSelected))
        {
            DangerSelectedCount = DangerItems.Count(i => i.IsSelected);
        }
    }

    private bool CanRefresh() => !IsBusy;

    private bool CanRunDanger() => !IsBusy && DangerSelectedCount > 0;

    private bool CanCreateBackup() => !IsBusy;

    private static string FormatCount(int count) => count switch
    {
        0 => "пусто",
        1 => "1 запись",
        _ => $"{count:N0} записей",
    };

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
