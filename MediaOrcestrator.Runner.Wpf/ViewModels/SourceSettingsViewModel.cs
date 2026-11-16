using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

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

    [ObservableProperty]
    private bool _isQuotaSupported;

    [ObservableProperty]
    private string _quotaStatusText = string.Empty;

    [ObservableProperty]
    private bool _isQuotaExhausted;

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

    partial void OnNameChanged(string value)
    {
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(Name);

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
        UpdateQuotaStatus();

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
}
