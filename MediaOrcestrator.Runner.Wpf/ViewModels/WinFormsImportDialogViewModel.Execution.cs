using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain.Migration;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class WinFormsImportDialogViewModel
{
    public void InspectSource()
    {
        ActiveInspectionTask = InspectSourceAsync();
    }

    private async Task InspectSourceAsync()
    {
        CancelInspectionCore();
        CancelWaitingCore();

        var requestedPath = SourceFolderPath;
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            ClearInspectionState();
            return;
        }

        var cts = new CancellationTokenSource();
        _inspectCts = cts;
        var appBase = CurrentAppBaseDirectory;

        WinFormsInspectionResult result;
        try
        {
            result = await Task.Run(() =>
            {
                cts.Token.ThrowIfCancellationRequested();
                return _inspector.Inspect(requestedPath, appBase);
            }, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested || _isClosed || !ReferenceEquals(_inspectCts, cts))
            {
                return;
            }

            _logger.LogWarning(ex, "Сбой инспекции папки WinForms {Path}", requestedPath);
            ClearInspectionState(ex.Message);
            return;
        }
        finally
        {
            if (ReferenceEquals(_inspectCts, cts))
            {
                _inspectCts = null;
            }

            cts.Dispose();
        }

        if (_isClosed || !string.Equals(SourceFolderPath, requestedPath, StringComparison.Ordinal))
        {
            return;
        }

        LastInspection = result;
        IsSourceValid = result.IsValid;
        InspectionErrorMessage = result.IsValid ? null : result.ErrorMessage;
        DetectedVersionText = result.DetectedVersion?.ToString() ?? "–";
        IsSourceRunning = result.IsValid && result.ProcessStatus.IsBusy;
        RunningPidsText = FormatRunningStatus(result.ProcessStatus);
        Summary = result.Summary;

        if (IsSourceRunning && result.Settings is not null)
        {
            ActiveWaitTask = WaitForProcessExitAsync(result.CanonicalPath, result.Settings.DatabasePath);
        }
    }

    private void ClearInspectionState(string? errorMessage = null)
    {
        LastInspection = null;
        IsSourceValid = false;
        InspectionErrorMessage = errorMessage;
        DetectedVersionText = "–";
        IsSourceRunning = false;
        RunningPidsText = string.Empty;
        Summary = null;
    }

    private bool CanRetryWaiting() => IsSourceRunning && !IsWaitingForExit && !IsImporting && !IsCompleted;

    [RelayCommand(CanExecute = nameof(CanRetryWaiting))]
    private void StartWaitingForExit()
    {
        if (LastInspection is { IsValid: true, Settings: not null } inspection)
        {
            CancelWaitingCore();
            ActiveWaitTask = WaitForProcessExitAsync(inspection.CanonicalPath, inspection.Settings.DatabasePath);
        }
        else
        {
            InspectSource();
        }
    }

    [RelayCommand(CanExecute = nameof(IsWaitingForExit))]
    private void CancelWaiting()
    {
        CancelWaitingCore();
    }

    private async Task WaitForProcessExitAsync(string canonicalSource, string databasePath)
    {
        var cts = new CancellationTokenSource();
        _waitCts = cts;
        IsWaitingForExit = true;

        try
        {
            await _processDetector.WaitForCloseAsync(canonicalSource, databasePath, ProcessPollInterval, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка ожидания закрытия WinForms в {Path}", canonicalSource);
            return;
        }
        finally
        {
            if (ReferenceEquals(_waitCts, cts))
            {
                _waitCts = null;
                IsWaitingForExit = false;
            }
        }

        var wasCancelled = cts.IsCancellationRequested;
        cts.Dispose();

        if (wasCancelled || _isClosed)
        {
            return;
        }

        if (string.Equals(LastInspection?.CanonicalPath, canonicalSource, StringComparison.OrdinalIgnoreCase))
        {
            await InspectSourceAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartImport))]
    private async Task StartImportAsync()
    {
        _importCts?.Dispose();
        _importCts = new();
        var token = _importCts.Token;

        IsImporting = true;
        ErrorMessage = null;
        ProgressPercent = 0;
        ProgressStatusText = "Подготовка переноса…";

        var progress = new Progress<WinFormsImportProgress>(update =>
        {
            ProgressStatusText = update.Message;
            if (update.Percent is double percent)
            {
                ProgressPercent = percent;
            }
        });

        try
        {
            if (IsSeparateInstanceMode)
            {
                var options = new WinFormsSeparateInstanceOptions(
                    SourceFolderPath,
                    TargetFolderPath,
                    CreateShortcut: CreateShortcut,
                    ShortcutName: string.IsNullOrWhiteSpace(ShortcutName) ? null : ShortcutName.Trim(),
                    EnableAutoSync: EnableAutoSync,
                    LaunchAfterCreate: LaunchAfterCreate,
                    CurrentAppBaseDirectory: CurrentAppBaseDirectory,
                    UpdateRepo: _updates.Repo);

                var result = await _importService.CreateSeparateInstanceAsync(options, progress, token);
                SeparateResult = result;
                IsCompleted = true;
                ProgressPercent = 100;
                ProgressStatusText = "Отдельный экземпляр WPF создан.";

                if (LaunchAfterCreate && !_shellLauncher.Start(result.ExecutablePath))
                {
                    _logger.LogWarning("Не удалось автоматически запустить новый экземпляр {Exe}", result.ExecutablePath);
                    ErrorMessage = "Экземпляр создан, но запустить его сразу не получилось – откройте папку и запустите вручную.";
                }

                return;
            }

            var replaceOptions = new WinFormsReplaceCurrentOptions(
                SourceFolderPath,
                EnableAutoSync: EnableAutoSync,
                TargetAppBaseDirectory: CurrentAppBaseDirectory,
                UpdateRepo: _updates.Repo,
                CurrentDatabase: _currentDatabase,
                CurrentDatabasePath: _currentPaths?.DatabasePath,
                CurrentStatePath: _currentPaths?.StatePath);

            var replaceResult = await _importService.StageReplaceCurrentAsync(replaceOptions, progress, token);
            if (token.IsCancellationRequested)
            {
                WinFormsImportService.CancelPendingReplace(replaceResult);
                throw new OperationCanceledException(token);
            }

            ProgressPercent = 100;
            ProgressStatusText = "Перезапуск приложения для применения данных…";

            if (!_shellLauncher.Start(replaceResult.ExecutablePath, replaceResult.RestartArguments))
            {
                _logger.LogError("Не удалось запустить перезапуск {Exe} {Args}", replaceResult.ExecutablePath, replaceResult.RestartArguments);
                ErrorMessage = "Снимок подготовлен, но перезапустить приложение автоматически не удалось. Закройте и запустите его вручную.";
                return;
            }

            _isClosed = true;
            _lifetime.Shutdown();
            RequestClose?.Invoke(this, true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _logger.LogInformation("Перенос из WinForms отменён пользователем");
            ProgressStatusText = "Перенос отменён.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при переносе из WinForms {Source}", SourceFolderPath);
            ErrorMessage = ex.Message;
            ProgressStatusText = string.Empty;
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsImporting))]
    private void CancelImport()
    {
        try
        {
            _importCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void DismissFirstRun()
    {
        _isClosed = true;
        CancelInspectionCore();
        CancelWaitingCore();
        _settingsStore.SetBool(FirstRunImportDetector.PromptDismissedKey, true);
        _settingsStore.Flush();
        RequestClose?.Invoke(this, false);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Cancel()
    {
        _isClosed = true;
        CancelInspectionCore();
        CancelWaitingCore();
        RequestClose?.Invoke(this, false);
    }

    [RelayCommand]
    private void OpenTargetFolder()
    {
        if (SeparateResult is { } result && !_shellLauncher.Open(result.TargetFolder))
        {
            _logger.LogWarning("Не удалось открыть папку нового экземпляра {Path}", result.TargetFolder);
        }
    }

    [RelayCommand]
    private void LaunchCreatedInstance()
    {
        if (SeparateResult is { } result && !_shellLauncher.Start(result.ExecutablePath))
        {
            _logger.LogWarning("Не удалось запустить созданный экземпляр {Exe}", result.ExecutablePath);
            ErrorMessage = "Не удалось запустить новый экземпляр.";
        }
    }

    [RelayCommand]
    private void Finish()
    {
        _isClosed = true;
        CancelInspectionCore();
        CancelWaitingCore();
        RequestClose?.Invoke(this, true);
    }

    private void CancelInspectionCore()
    {
        var cts = _inspectCts;
        _inspectCts = null;

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void CancelWaitingCore()
    {
        var cts = _waitCts;
        _waitCts = null;
        IsWaitingForExit = false;

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static string FormatRunningStatus(WinFormsProcessStatus status)
    {
        if (!status.IsBusy)
        {
            return string.Empty;
        }

        if (status.RunningProcessIds.Count > 0)
        {
            return "PID " + string.Join(", ", status.RunningProcessIds);
        }

        return "файл базы данных занят";
    }
}
