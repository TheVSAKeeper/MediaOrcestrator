using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class DatabaseCleanupViewModel
{
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

    partial void OnIsBackupsExpandedChanged(bool value) => ApplyVisibleBackups();

    [RelayCommand]
    private void ToggleBackupsExpanded() => IsBackupsExpanded = !IsBackupsExpanded;

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

    private bool CanCreateBackup() => !IsBusy;

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
}
