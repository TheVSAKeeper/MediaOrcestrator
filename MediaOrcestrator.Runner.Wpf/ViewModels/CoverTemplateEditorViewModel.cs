using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;
using MediaOrcestrator.Runner.Wpf.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class CoverTemplateEditorViewModel : ObservableObject
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    private readonly CoverGenerator _generator;
    private readonly CoverTemplateStore _store;
    private readonly IDialogService _dialogs;
    private readonly IFilePicker _filePicker;
    private readonly IUiTimer _previewTimer;
    private readonly ILogger<CoverTemplateEditorViewModel> _logger;

    private bool _suppressPreview;
    private int _previewGeneration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTemplateImage))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _templatePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSequential))]
    [NotifyPropertyChangedFor(nameof(IsTitleRegex))]
    private CoverNumberMode _numberMode = CoverNumberMode.Sequential;

    [ObservableProperty]
    private double _startNumber = 1;

    [ObservableProperty]
    private string _titleRegexPattern = CoverTemplate.DefaultTitleRegex;

    [ObservableProperty]
    private double _sampleNumber = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLayer))]
    [NotifyCanExecuteChangedFor(nameof(RemoveLayerCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveLayerUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveLayerDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickFillColorCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickStrokeColorCommand))]
    private CoverTextLayerViewModel? _selectedLayer;

    [ObservableProperty]
    private ColorPickerViewModel? _fillColorPicker;

    [ObservableProperty]
    private ColorPickerViewModel? _strokeColorPicker;

    [ObservableProperty]
    private bool _isFillColorPickerOpen;

    [ObservableProperty]
    private bool _isStrokeColorPickerOpen;

    [ObservableProperty]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private string? _previewError;

    [ObservableProperty]
    private string? _sizeWarning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private bool _isTemplateSectionExpanded = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveStatus))]
    private string? _saveStatusText;

    [ObservableProperty]
    private bool _isDirty;

    public CoverTemplateEditorViewModel(
        CoverGenerator generator,
        CoverTemplateStore store,
        IDialogService dialogs,
        IFilePicker filePicker,
        IUiDispatcher dispatcher,
        CoverTemplate? initial,
        string? profileName,
        string? sampleTitle,
        ILogger<CoverTemplateEditorViewModel> logger)
    {
        _generator = generator;
        _store = store;
        _dialogs = dialogs;
        _filePicker = filePicker;
        _logger = logger;

        _previewTimer = dispatcher.CreateTimer(TimeSpan.FromMilliseconds(60), OnPreviewTimerTick);

        FontStyles =
        [
            new("Жирный", CoverFontStyle.Bold),
            new("Обычный", CoverFontStyle.Regular),
            new("Курсив", CoverFontStyle.Italic),
            new("Жирный курсив", CoverFontStyle.BoldItalic),
        ];

        FontFamilies = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ProfileName = profileName ?? string.Empty;

        _suppressPreview = true;
        try
        {
            LoadFrom(initial);

            if (!string.IsNullOrEmpty(sampleTitle) && initial is not null)
            {
                SampleNumber = CoverNumberResolver.Resolve(initial, sampleTitle, 0);
            }
        }
        finally
        {
            _suppressPreview = false;
        }

        IsTemplateSectionExpanded = !(HasTemplateImage && !string.IsNullOrWhiteSpace(ProfileName));

        RefreshPreview();
        IsDirty = false;
    }

    public event EventHandler? CloseRequested;

    public ObservableCollection<CoverTextLayerViewModel> Layers { get; } = [];

    public IReadOnlyList<FontStyleOption> FontStyles { get; }

    public IReadOnlyList<string> FontFamilies { get; }

    public CoverTemplate? ResultTemplate { get; private set; }

    public string? ResultProfileName { get; private set; }

    public bool IsSequential => NumberMode == CoverNumberMode.Sequential;

    public bool IsTitleRegex => NumberMode == CoverNumberMode.TitleRegex;

    public bool HasTemplateImage => !string.IsNullOrEmpty(TemplatePath) && File.Exists(TemplatePath);

    public bool HasSelectedLayer => SelectedLayer is not null;

    public bool HasSaveStatus => !string.IsNullOrEmpty(SaveStatusText);

    [RelayCommand]
    private void BrowseTemplate()
    {
        var request = new FileOpenRequest(
            "Выберите картинку-шаблон обложки",
            "Изображения|" + string.Join(";", ImageExtensions.Select(e => "*" + e)) + "|Все файлы|*.*");

        var path = _filePicker.PickFile(request);

        if (path is not null)
        {
            TemplatePath = path;
        }
    }

    [RelayCommand]
    private void SetNumberMode(CoverNumberMode mode) => NumberMode = mode;

    [RelayCommand]
    private void AddLayer()
    {
        var layer = CoverTextLayerViewModel.FromDomain(CoverTemplate.DefaultNumberLayer);
        AttachLayer(layer);
        Layers.Add(layer);
        SelectedLayer = layer;
        NotifyLayerCommands();
        RefreshPreview();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveLayer))]
    private void RemoveLayer()
    {
        if (SelectedLayer is not { } layer)
        {
            return;
        }

        var index = Layers.IndexOf(layer);
        DetachLayer(layer);
        Layers.Remove(layer);
        SelectedLayer = Layers.Count == 0 ? null : Layers[Math.Min(index, Layers.Count - 1)];
        NotifyLayerCommands();
        RefreshPreview();
    }

    private bool CanRemoveLayer() => SelectedLayer is not null && Layers.Count > 1;

    [RelayCommand(CanExecute = nameof(CanMoveLayerUp))]
    private void MoveLayerUp()
    {
        var index = SelectedLayer is null ? -1 : Layers.IndexOf(SelectedLayer);

        if (index > 0)
        {
            Layers.Move(index, index - 1);
            NotifyLayerCommands();
            RefreshPreview();
        }
    }

    private bool CanMoveLayerUp() => SelectedLayer is not null && Layers.IndexOf(SelectedLayer) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveLayerDown))]
    private void MoveLayerDown()
    {
        var index = SelectedLayer is null ? -1 : Layers.IndexOf(SelectedLayer);

        if (index >= 0 && index < Layers.Count - 1)
        {
            Layers.Move(index, index + 1);
            NotifyLayerCommands();
            RefreshPreview();
        }
    }

    private bool CanMoveLayerDown()
    {
        var index = SelectedLayer is null ? -1 : Layers.IndexOf(SelectedLayer);
        return index >= 0 && index < Layers.Count - 1;
    }

    [RelayCommand]
    private void IncreaseStartNumber() => StartNumber = Math.Max(0, StartNumber + 1);

    [RelayCommand]
    private void DecreaseStartNumber() => StartNumber = Math.Max(0, StartNumber - 1);

    [RelayCommand]
    private void IncreaseSampleNumber() => SampleNumber = Math.Max(0, SampleNumber + 1);

    [RelayCommand]
    private void DecreaseSampleNumber() => SampleNumber = Math.Max(0, SampleNumber - 1);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var name = ProfileName.Trim();

        if (string.IsNullOrEmpty(name) || !HasTemplateImage)
        {
            return;
        }

        if (_store.Exists(name)
            && !string.Equals(name, ResultProfileName, StringComparison.OrdinalIgnoreCase)
            && !_dialogs.Confirm("Перезаписать профиль",
                $"Профиль «{name}» уже существует. Перезаписать его?"))
        {
            return;
        }

        var template = BuildTemplate();

        if (!_store.Save(name, template))
        {
            _dialogs.Error("Не удалось сохранить", $"Профиль «{name}» не сохранён. Подробности – в логах.");
            return;
        }

        ResultTemplate = template;
        ResultProfileName = name;
        IsDirty = false;
        SaveStatusText = $"Профиль «{name}» сохранён.";
    }

    private bool CanSave() => HasTemplateImage && !string.IsNullOrWhiteSpace(ProfileName);

    partial void OnProfileNameChanged(string value)
    {
        if (!_suppressPreview)
        {
            IsDirty = true;
            SaveStatusText = null;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void NotifyLayerCommands()
    {
        RemoveLayerCommand.NotifyCanExecuteChanged();
        MoveLayerUpCommand.NotifyCanExecuteChanged();
        MoveLayerDownCommand.NotifyCanExecuteChanged();
    }
}

public sealed record FontStyleOption(string Label, CoverFontStyle Value)
{
    public override string ToString() => Label;
}
