using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public enum SourceSettingsDialogMode
{
    None = 0,
    Add = 1,
    Edit = 2,
    Duplicate = 3,
}

public sealed partial class SourceSettingsViewModel : ObservableObject, IDialogViewModel
{
    private const string LastDonorKeyPrefix = "wpf.sources.copy.last_donor.";

    private readonly StateManager _stateManager;
    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly IAuthUI _authUi;
    private readonly ISettingsStore _settings;
    private readonly ILogger<SourceSettingsViewModel> _logger;
    private readonly IShellLauncher _shellLauncher;
    private readonly IFilePicker _filePicker;
    private ISourceType? _plugin;
    private Source? _editSource;
    private Source? _templateSource;
    private string? _pendingSourceId;
    private List<Source> _availableSources = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private SourceSettingsDialogMode _mode = SourceSettingsDialogMode.Add;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _pluginDisplayName;

    [ObservableProperty]
    private bool _isAuthSupported;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private string _authStatusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AuthenticateCommand))]
    private bool _isAuthenticating;

    [ObservableProperty]
    private bool _isCopySupported;

    [ObservableProperty]
    private bool _isCopyPickerOpen;

    [ObservableProperty]
    private Source? _copyPickerSelected;

    [ObservableProperty]
    private bool _copyIncludesAuth = true;

    [ObservableProperty]
    private bool _hasDocs;

    [ObservableProperty]
    private bool _allowBack;

    [ObservableProperty]
    private bool _wasBackRequested;

    public SourceSettingsViewModel(
        StateManager stateManager,
        ActionHolder actionHolder,
        IDialogService dialogs,
        IAuthUI authUi,
        ISettingsStore settings,
        ILogger<SourceSettingsViewModel> logger,
        IShellLauncher shellLauncher,
        IFilePicker filePicker)
    {
        _stateManager = stateManager;
        _actionHolder = actionHolder;
        _dialogs = dialogs;
        _authUi = authUi;
        _settings = settings;
        _logger = logger;
        _shellLauncher = shellLauncher;
        _filePicker = filePicker;

        Fields = [];
        CopyableSources = [];
        DocFiles = [];
    }

    public event EventHandler<bool>? RequestClose;

    public ObservableCollection<SettingFieldViewModel> Fields { get; }

    public ObservableCollection<Source> CopyableSources { get; }

    public List<string> DocFiles { get; private set; }

    public Dictionary<string, string>? Settings { get; private set; }

    public string Title => Mode switch
    {
        SourceSettingsDialogMode.Edit => "Редактирование источника",
        SourceSettingsDialogMode.Duplicate => "Дублирование источника",
        _ => "Новый источник",
    };

    public string SaveButtonText => Mode switch
    {
        SourceSettingsDialogMode.Edit => "Сохранить изменения",
        SourceSettingsDialogMode.Duplicate => "Создать копию",
        _ => "Создать",
    };

    public void InitializeAdd(ISourceType plugin, string newSourceId, IEnumerable<Source> availableSources)
    {
        Mode = SourceSettingsDialogMode.Add;
        _plugin = plugin;
        _editSource = null;
        _templateSource = null;
        _pendingSourceId = newSourceId;
        _availableSources = availableSources.ToList();
        Name = string.Empty;
        WasBackRequested = false;
        Settings = null;
        BuildFields();
        RefreshContext();
    }

    public void InitializeEdit(Source source, ISourceType plugin, IEnumerable<Source> availableSources)
    {
        Mode = SourceSettingsDialogMode.Edit;
        _plugin = plugin;
        _editSource = source;
        _templateSource = null;
        _pendingSourceId = source.Id;
        _availableSources = availableSources.ToList();
        Name = source.Title;
        BuildFields();
        RefreshContext();
    }

    public void InitializeDuplicate(Source template, ISourceType plugin, string newSourceId, IEnumerable<Source> availableSources)
    {
        Mode = SourceSettingsDialogMode.Duplicate;
        _plugin = plugin;
        _editSource = null;
        _templateSource = template;
        _pendingSourceId = newSourceId;
        _availableSources = availableSources.ToList();
        Name = template.Title + " (копия)";
        BuildFields();
        ApplyCopiedSettings(template, false);
        RefreshContext();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (_plugin is null)
        {
            return;
        }

        var settings = BuildSettingsDictionary();
        settings["_system_name"] = Name.Trim();
        Settings = settings;
        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Settings = null;
        WasBackRequested = false;
        RequestClose?.Invoke(this, false);
    }

    [RelayCommand]
    private void Back()
    {
        Settings = null;
        WasBackRequested = true;
        RequestClose?.Invoke(this, false);
    }

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
    private void CancelCopy()
    {
        IsCopyPickerOpen = false;
    }

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
        catch (OperationCanceledException)
        {
            action.MarkCancelled("Отменено");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Сбой авторизации в плагине {Plugin}", _plugin.Name);
            action.Fail("Ошибка: " + ex.Message, ex);
            _dialogs.Error("Авторизация", $"Не удалось завершить авторизацию: {ex.Message}");
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    private bool CanAuthenticate()
    {
        return IsAuthSupported && !IsAuthenticating;
    }

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
            field.ReplaceOptions(options);
            action.Finish($"Получено: {options.Count}");
        }
        catch (OperationCanceledException)
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

    private bool CanOpenDocs()
    {
        return HasDocs;
    }

    partial void OnHasDocsChanged(bool value)
    {
        OpenDocsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsAuthSupportedChanged(bool value)
    {
        AuthenticateCommand.NotifyCanExecuteChanged();
    }

    private bool HasFilledFields()
    {
        foreach (var field in Fields)
        {
            if (!string.IsNullOrWhiteSpace(field.Value))
            {
                return true;
            }
        }

        return false;
    }

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

    partial void OnNameChanged(string value)
    {
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave()
    {
        return !string.IsNullOrWhiteSpace(Name);
    }

    private void BuildFields()
    {
        Fields.Clear();

        if (_plugin is null)
        {
            return;
        }

        var initialSettings = _editSource?.Settings;

        foreach (var setting in _plugin.SettingsKeys)
        {
            var field = new SettingFieldViewModel(setting, _filePicker);
            var initialValue = initialSettings is not null && initialSettings.TryGetValue(setting.Key, out var saved)
                ? saved
                : null;

            field.SetInitialValue(initialValue);
            Fields.Add(field);
        }
    }

    private void RefreshContext()
    {
        PluginDisplayName = _plugin?.Name;
        IsAuthSupported = _plugin is IAuthenticatable;
        UpdateAuthStatus();

        CopyableSources.Clear();

        if (_plugin is not null)
        {
            var candidates = _availableSources
                .Where(s => string.Equals(s.TypeId, _plugin.Name, StringComparison.Ordinal)
                            && (_editSource is null || s.Id != _editSource.Id))
                .OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase);

            foreach (var candidate in candidates)
            {
                CopyableSources.Add(candidate);
            }
        }

        IsCopySupported = CopyableSources.Count > 0;
        CopyPickerSelected = CopyableSources.FirstOrDefault();

        DocFiles = FindDocFiles();
        HasDocs = DocFiles.Count > 0;
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

    private Dictionary<string, string> BuildSettingsDictionary()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_editSource is not null)
        {
            foreach (var (key, value) in _editSource.Settings)
            {
                if (key.StartsWith("_system_", StringComparison.Ordinal))
                {
                    snapshot[key] = value;
                }
            }
        }
        else if (_pendingSourceId is not null && _plugin is IAuthenticatable)
        {
            snapshot["_system_state_path"] = _stateManager.GetSourceStatePath(_pendingSourceId);
        }

        foreach (var field in Fields)
        {
            snapshot[field.Key] = field.Value ?? string.Empty;
        }

        return snapshot;
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
