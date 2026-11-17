using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.ViewModels.Settings;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public enum SettingPathKind
{
    None = 0,
    Folder = 1,
    File = 2,
}

public sealed partial class PathSettingViewModel : ObservableValidator
{
    private readonly ISettingsStore _store;
    private readonly IFilePicker _filePicker;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [Required(ErrorMessage = "Путь обязателен")]
    private string _editValue = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _storedValue = string.Empty;

    public PathSettingViewModel(
        ISettingsStore store,
        string key,
        string title,
        string description,
        string fallbackPath,
        string factoryValue,
        SettingPathKind kind,
        IFilePicker filePicker)
    {
        _store = store;
        _filePicker = filePicker;
        Key = key;
        Title = title;
        Description = description;
        FactoryValue = factoryValue;
        Kind = kind;

        ErrorsChanged += OnErrorsChanged;

        var raw = store.GetStringValue(key);
        StoredValue = string.IsNullOrWhiteSpace(raw) ? fallbackPath : raw;
        EditValue = StoredValue;
        ValidateAllProperties();
    }

    public string Key { get; }

    public string Title { get; }

    public string Description { get; }

    public string FactoryValue { get; }

    public SettingPathKind Kind { get; }

    public SettingsResetField? ResetField { get; internal set; }

    public bool IsDirty => !string.Equals(EditValue?.Trim() ?? string.Empty,
        StoredValue ?? string.Empty,
        StringComparison.Ordinal);

    public string? EditValueError => GetErrors(nameof(EditValue))
        .OfType<ValidationResult>()
        .Select(r => r.ErrorMessage)
        .FirstOrDefault();

    public bool TryCommit()
    {
        ValidateAllProperties();

        if (HasErrors)
        {
            return false;
        }

        var trimmed = EditValue?.Trim() ?? string.Empty;

        if (string.Equals(trimmed, StoredValue, StringComparison.Ordinal))
        {
            return true;
        }

        _store.SetValue(Key, trimmed);
        StoredValue = trimmed;
        EditValue = trimmed;
        return true;
    }

    public void Reset()
    {
        EditValue = StoredValue;
    }

    [RelayCommand]
    private void Browse()
    {
        switch (Kind)
        {
            case SettingPathKind.Folder:
                BrowseFolder();
                break;
            case SettingPathKind.File:
                BrowseFile();
                break;
        }
    }

    private void BrowseFolder()
    {
        var seed = EditValue?.Trim() ?? string.Empty;
        var initialDirectory = !string.IsNullOrEmpty(seed) && Directory.Exists(seed) ? seed : null;

        var path = _filePicker.PickFolder($"Выберите папку: {Title}", initialDirectory);

        if (path is not null)
        {
            EditValue = path;
        }
    }

    private void BrowseFile()
    {
        var seed = EditValue?.Trim() ?? string.Empty;
        var request = new FileOpenRequest($"Выберите файл: {Title}", string.Empty) { CheckFileExists = false };

        if (!string.IsNullOrEmpty(seed))
        {
            var dir = Path.GetDirectoryName(seed);

            request = request with
            {
                InitialDirectory = !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null,
                FileName = Path.GetFileName(seed),
            };
        }

        var path = _filePicker.PickFile(request);

        if (path is not null)
        {
            EditValue = path;
        }
    }

    private void OnErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditValue))
        {
            OnPropertyChanged(nameof(EditValueError));
        }
    }
}
