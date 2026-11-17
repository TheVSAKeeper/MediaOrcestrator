using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Modules;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class SettingFieldViewModel : ObservableObject
{
    private readonly IFilePicker _filePicker;
    private bool _suppressSelectedChange;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _isSecretVisible;

    [ObservableProperty]
    private SettingOption? _selectedOption;

    [ObservableProperty]
    private bool _isLoadingOptions;

    [ObservableProperty]
    private bool _canLoadOptions;

    public SettingFieldViewModel(SourceSettings setting, IFilePicker filePicker)
    {
        _filePicker = filePicker;
        Setting = setting;
        Options = [];
        CanLoadOptions = setting.Type == SettingType.Dropdown && setting.Options is null;

        if (setting.Type == SettingType.Dropdown && setting.Options is { Count: > 0 })
        {
            foreach (var option in setting.Options)
            {
                Options.Add(option);
            }
        }
    }

    public SourceSettings Setting { get; }

    public string Key => Setting.Key;

    public string Title => FormatDisplayTitle(Setting.Title);

    public string LabelWithRequired => Setting.IsRequired ? Title + " *" : Title;

    private static string FormatDisplayTitle(string? title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return string.Empty;
        }

        return char.IsLower(title[0])
            ? char.ToUpper(title[0], CultureInfo.CurrentCulture) + title[1..]
            : title;
    }

    public string? Description => Setting.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Setting.Description);

    public bool IsRequired => Setting.IsRequired;

    public bool IsText => Setting.Type is SettingType.Text or SettingType.None;

    public bool IsSecret => Setting.Type == SettingType.Secret;

    public bool IsFolderPath => Setting.Type == SettingType.FolderPath;

    public bool IsFilePath => Setting.Type == SettingType.FilePath;

    public bool IsDropdown => Setting.Type == SettingType.Dropdown;

    public ObservableCollection<SettingOption> Options { get; }

    public void SetInitialValue(string? value)
    {
        var effective = value ?? Setting.DefaultValue ?? string.Empty;

        if (IsDropdown)
        {
            EnsureValueInOptions(effective);
            _suppressSelectedChange = true;

            try
            {
                SelectedOption = Options.FirstOrDefault(o => string.Equals(o.Value, effective, StringComparison.Ordinal));
                Value = SelectedOption?.Value ?? effective;
            }
            finally
            {
                _suppressSelectedChange = false;
            }
        }
        else
        {
            Value = effective;
        }
    }

    public void ReplaceOptions(IEnumerable<SettingOption> options)
    {
        Options.Clear();

        foreach (var option in options)
        {
            Options.Add(option);
        }

        if (Options.Count == 0)
        {
            SelectedOption = null;
            return;
        }

        var currentValue = Value;
        var match = Options.FirstOrDefault(o => string.Equals(o.Value, currentValue, StringComparison.Ordinal));
        SelectedOption = match ?? Options[0];
    }

    [RelayCommand]
    private void ToggleSecret()
    {
        IsSecretVisible = !IsSecretVisible;
    }

    [RelayCommand]
    private void BrowsePath()
    {
        if (IsFolderPath)
        {
            var initialDirectory = !string.IsNullOrEmpty(Value) && Directory.Exists(Value) ? Value : null;
            var folder = _filePicker.PickFolder($"Выберите папку: {Title}", initialDirectory);

            if (folder is not null)
            {
                Value = folder;
            }

            return;
        }

        if (!IsFilePath)
        {
            return;
        }

        var request = new FileOpenRequest($"Выберите файл: {Title}", string.Empty) { CheckFileExists = false };

        if (!string.IsNullOrEmpty(Value))
        {
            var dir = Path.GetDirectoryName(Value);

            request = request with
            {
                InitialDirectory = !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null,
                FileName = Path.GetFileName(Value),
            };
        }

        var path = _filePicker.PickFile(request);

        if (path is not null)
        {
            Value = path;
        }
    }

    partial void OnSelectedOptionChanged(SettingOption? value)
    {
        if (_suppressSelectedChange)
        {
            return;
        }

        Value = value?.Value ?? string.Empty;
    }

    private void EnsureValueInOptions(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (Options.Any(o => string.Equals(o.Value, value, StringComparison.Ordinal)))
        {
            return;
        }

        Options.Add(new() { Value = value, Label = value });
    }
}
