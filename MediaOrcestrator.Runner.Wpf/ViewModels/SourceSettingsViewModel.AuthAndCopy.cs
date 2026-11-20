using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class SourceSettingsViewModel
{
    [RelayCommand]
    private void OpenCopyPicker()
    {
        if (!IsCopySupported)
        {
            return;
        }

        var savedDonorId = GetLastDonorId();

        if (savedDonorId is not null)
        {
            var saved = CopyableSources.FirstOrDefault(s => s.Id == savedDonorId);

            if (saved is not null)
            {
                CopyPickerSelected = saved;
            }
        }

        CopyPickerSelected ??= CopyableSources.FirstOrDefault();
        IsCopyPickerOpen = true;
    }

    [RelayCommand]
    private void ApplyCopy()
    {
        if (CopyPickerSelected is null)
        {
            return;
        }

        if (HasFilledFields()
            && !_dialogs.Confirm("Скопировать настройки",
                $"Заполненные поля будут перезаписаны значениями из «{CopyPickerSelected.Title}». Продолжить?",
                true))
        {
            return;
        }

        var donor = CopyPickerSelected;
        ApplyCopiedSettings(donor, CopyIncludesAuth);
        SaveLastDonorId(donor.Id);
        IsCopyPickerOpen = false;
        _logger.LogInformation("Скопированы настройки из {Donor} ({Type})", donor.Title, donor.TypeId);
    }

    [RelayCommand]
    private void CancelCopy() => IsCopyPickerOpen = false;

    [RelayCommand(CanExecute = nameof(CanAuthenticate))]
    private async Task AuthenticateAsync()
    {
        if (_plugin is not IAuthenticatable auth)
        {
            return;
        }

        IsAuthenticating = true;

        using var cts = new CancellationTokenSource();
        var action = _actionHolder.Register($"Авторизация: {_plugin.Name}",
            "Открытие браузера…",
            0,
            cts,
            kind: ActionKind.Other);

        try
        {
            var snapshot = BuildSettingsDictionary();
            await Task.Run(() => auth.AuthenticateAsync(snapshot, _authUi, cts.Token), cts.Token);

            foreach (var field in Fields)
            {
                if (snapshot.TryGetValue(field.Key, out var updated) && updated != (field.Value ?? string.Empty))
                {
                    field.SetInitialValue(updated);
                }
            }

            UpdateAuthStatus();
            action.Finish(IsAuthenticated ? "Авторизация сохранена" : "Авторизация не подтверждена");
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested || ex is AuthenticationDismissedException)
        {
            action.MarkCancelled("Отменено");
        }
        catch (Exception ex)
        {
            WpfAuthUI.LogFailure(_logger, ex, $"Сбой авторизации в плагине {_plugin.Name}");
            var message = ex is OperationCanceledException or TimeoutException
                ? "Истекло время ожидания ответа. Попробуйте войти ещё раз."
                : "Не удалось завершить авторизацию. Подробности в журнале.";
            action.Fail(message);
            _dialogs.Error("Авторизация", message);
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    private bool CanAuthenticate() => IsAuthSupported && !IsAuthenticating;

    [RelayCommand]
    private async Task LoadOptionsAsync(SettingFieldViewModel? field)
    {
        if (field is null || _plugin is null || !field.IsDropdown)
        {
            return;
        }

        field.IsLoadingOptions = true;

        using var cts = new CancellationTokenSource();
        var action = _actionHolder.Register($"Загрузка вариантов: {field.Title}",
            "Запрос плагина…",
            0,
            cts,
            kind: ActionKind.Metadata);

        try
        {
            var currentSettings = BuildSettingsDictionary();
            var options = await _plugin.GetSettingOptionsAsync(field.Key, currentSettings);
            cts.Token.ThrowIfCancellationRequested();
            field.ReplaceOptions(options);
            action.Finish($"Получено: {options.Count}");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            action.MarkCancelled("Отменено");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось загрузить варианты для {Key}", field.Key);
            action.Fail("Ошибка: " + ex.Message, ex);
        }
        finally
        {
            field.IsLoadingOptions = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenDocs))]
    private async Task OpenDocsAsync()
    {
        if (DocFiles.Count == 0 || _plugin is null)
        {
            return;
        }

        var dialog = new DocumentationDialogViewModel($"Руководство: {_plugin.Name}", DocFiles, shellLauncher: _shellLauncher);
        await _dialogs.ShowAsync(dialog);
    }

    private bool CanOpenDocs() => HasDocs;

    partial void OnHasDocsChanged(bool value) => OpenDocsCommand.NotifyCanExecuteChanged();

    partial void OnIsAuthSupportedChanged(bool value) => AuthenticateCommand.NotifyCanExecuteChanged();

    private bool HasFilledFields() => Fields.Any(field => !string.IsNullOrWhiteSpace(field.Value));

    private string? GetLastDonorId()
    {
        if (_plugin is null)
        {
            return null;
        }

        var raw = _settings.GetStringValue(LastDonorKeyPrefix + _plugin.Name);
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    private void SaveLastDonorId(string donorId)
    {
        if (_plugin is null)
        {
            return;
        }

        _settings.SetValue(LastDonorKeyPrefix + _plugin.Name, donorId);
    }

    private void UpdateAuthStatus()
    {
        if (_plugin is not IAuthenticatable auth)
        {
            IsAuthenticated = false;
            AuthStatusText = string.Empty;
            return;
        }

        try
        {
            var snapshot = BuildSettingsDictionary();
            IsAuthenticated = auth.IsAuthenticated(snapshot);
            AuthStatusText = IsAuthenticated ? "Авторизован" : "Не авторизован";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось проверить статус авторизации");
            IsAuthenticated = false;
            AuthStatusText = "Статус неизвестен";
        }
    }

    private void UpdateQuotaStatus()
    {
        QuotaSnapshot? snapshot = null;

        if (_plugin is IReportsQuota reporter)
        {
            try
            {
                snapshot = reporter.GetQuota(BuildSettingsDictionary());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось прочитать квоту источника");
            }
        }

        if (snapshot is null)
        {
            IsQuotaSupported = false;
            IsQuotaExhausted = false;
            QuotaStatusText = string.Empty;
            return;
        }

        IsQuotaSupported = true;
        IsQuotaExhausted = snapshot.ExhaustedUntil is { } until && until > DateTimeOffset.Now;
        QuotaStatusText = IsQuotaExhausted
            ? $"Исчерпана до {snapshot.ExhaustedUntil!.Value.ToLocalTime():dd MMM, HH:mm}"
            : $"≈ {snapshot.UsedUnits:N0} / {snapshot.DailyLimit:N0} ед.";
    }

    private void ApplyCopiedSettings(Source source, bool copyAuth)
    {
        foreach (var field in Fields)
        {
            if (source.Settings.TryGetValue(field.Key, out var value))
            {
                field.SetInitialValue(value);
            }
        }

        if (copyAuth && _plugin is IAuthenticatable)
        {
            TryCopyAuthState(source);
        }

        UpdateAuthStatus();
    }

    private void TryCopyAuthState(Source source)
    {
        var sourcePath = source.Settings.GetValueOrDefault("_system_state_path");
        var targetPath = GetTargetStatePath();

        if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetPath))
        {
            return;
        }

        if (!Directory.Exists(sourcePath))
        {
            _logger.LogWarning("Не найден каталог состояния шаблона: {Path}", sourcePath);
            return;
        }

        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(targetPath);

            foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourcePath, file);
                var dest = Path.Combine(targetPath, relative);
                var destDir = Path.GetDirectoryName(dest);

                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(file, dest, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось скопировать данные авторизации");
            _dialogs.Error("Источники", $"Не удалось скопировать данные авторизации: {ex.Message}");
        }
    }

    private string? GetTargetStatePath()
    {
        if (_editSource is not null)
        {
            return _editSource.Settings.GetValueOrDefault("_system_state_path");
        }

        return _pendingSourceId is null ? null : _stateManager.GetSourceStatePath(_pendingSourceId);
    }

    private List<string> FindDocFiles()
    {
        if (_plugin is null)
        {
            return [];
        }

        var assemblyDir = Path.GetDirectoryName(_plugin.GetType().Assembly.Location);

        if (string.IsNullOrEmpty(assemblyDir))
        {
            return [];
        }

        var docsDir = Path.Combine(assemblyDir, "docs");

        return Directory.Exists(docsDir)
            ? Directory.GetFiles(docsDir, "*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
            : [];
    }
}
