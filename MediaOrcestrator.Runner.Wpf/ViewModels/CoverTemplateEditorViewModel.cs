using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class CoverTemplateEditorViewModel : ObservableObject
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    private readonly CoverGenerator _generator;
    private readonly CoverTemplateStore _store;
    private readonly IDialogService _dialogs;
    private readonly IFilePicker _filePicker;
    private readonly DispatcherTimer _previewTimer;

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
        CoverTemplate? initial,
        string? profileName,
        string? sampleTitle)
    {
        _generator = generator;
        _store = store;
        _dialogs = dialogs;
        _filePicker = filePicker;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _previewTimer.Tick += OnPreviewTimerTick;

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

    [RelayCommand(CanExecute = nameof(HasSelectedLayer))]
    private void PickFillColor()
    {
        if (SelectedLayer is not { } layer)
        {
            return;
        }

        IsStrokeColorPickerOpen = false;
        FillColorPicker = BuildColorPicker("Цвет текста", layer.FillColorHex,
            hex => layer.FillColorHex = hex,
            () => IsFillColorPickerOpen = false);
        IsFillColorPickerOpen = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedLayer))]
    private void PickStrokeColor()
    {
        if (SelectedLayer is not { } layer)
        {
            return;
        }

        IsFillColorPickerOpen = false;
        StrokeColorPicker = BuildColorPicker("Цвет обводки", layer.StrokeColorHex,
            hex => layer.StrokeColorHex = hex,
            () => IsStrokeColorPickerOpen = false);
        IsStrokeColorPickerOpen = true;
    }

    private static ColorPickerViewModel BuildColorPicker(string title, string initialHex, Action<string> applyHex, Action close)
    {
        var picker = new ColorPickerViewModel(title, initialHex);

        picker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ColorPickerViewModel.Hex))
            {
                applyHex(picker.Hex);
            }
        };

        picker.CloseRequested += (_, _) => close();
        return picker;
    }

    partial void OnSelectedLayerChanged(CoverTextLayerViewModel? value)
    {
        IsFillColorPickerOpen = false;
        IsStrokeColorPickerOpen = false;
    }

    partial void OnIsFillColorPickerOpenChanged(bool value)
    {
        if (!value)
        {
            FillColorPicker = null;
        }
    }

    partial void OnIsStrokeColorPickerOpenChanged(bool value)
    {
        if (!value)
        {
            StrokeColorPicker = null;
        }
    }

    public void SetSelectedLayerPosition(double xPercent, double yPercent)
    {
        if (SelectedLayer is null)
        {
            return;
        }

        _suppressPreview = true;
        try
        {
            SelectedLayer.TextXPercent = Math.Round(Math.Clamp(xPercent, 0, 100), 1);
            SelectedLayer.TextYPercent = Math.Round(Math.Clamp(yPercent, 0, 100), 1);
        }
        finally
        {
            _suppressPreview = false;
        }

        RefreshPreview();
    }

    partial void OnTemplatePathChanged(string value) => RefreshPreview();

    partial void OnNumberModeChanged(CoverNumberMode value) => RefreshPreview();

    partial void OnStartNumberChanged(double value) => RefreshPreview();

    partial void OnTitleRegexPatternChanged(string value) => RefreshPreview();

    partial void OnSampleNumberChanged(double value) => RefreshPreview();

    private void LoadFrom(CoverTemplate? template)
    {
        foreach (var existing in Layers)
        {
            DetachLayer(existing);
        }

        Layers.Clear();

        if (template is null)
        {
            TemplatePath = string.Empty;
            NumberMode = CoverNumberMode.Sequential;
            StartNumber = 1;
            TitleRegexPattern = CoverTemplate.DefaultTitleRegex;

            var initialLayer = CoverTextLayerViewModel.FromDomain(CoverTemplate.DefaultNumberLayer);
            AttachLayer(initialLayer);
            Layers.Add(initialLayer);
            SelectedLayer = initialLayer;
            return;
        }

        TemplatePath = template.TemplatePath;
        NumberMode = template.NumberMode;
        StartNumber = template.StartNumber;
        TitleRegexPattern = string.IsNullOrEmpty(template.TitleRegexPattern)
            ? CoverTemplate.DefaultTitleRegex
            : template.TitleRegexPattern;

        var layers = template.Layers.Count > 0
            ? template.Layers
            : [CoverTemplate.DefaultNumberLayer];

        foreach (var layer in layers)
        {
            var vm = CoverTextLayerViewModel.FromDomain(layer);
            AttachLayer(vm);
            Layers.Add(vm);
        }

        SelectedLayer = Layers.FirstOrDefault();
    }

    private CoverTemplate BuildTemplate()
    {
        return new(TemplatePath,
            (int)Math.Round(StartNumber),
            NumberMode,
            TitleRegexPattern ?? string.Empty,
            Layers.Select(l => l.ToDomain()).ToList());
    }

    private void RefreshPreview()
    {
        if (_suppressPreview)
        {
            return;
        }

        IsDirty = true;
        SaveStatusText = null;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void OnPreviewTimerTick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        RenderPreview();
    }

    private void RenderPreview()
    {
        if (!HasTemplateImage)
        {
            PreviewImage = null;
            SizeWarning = null;
            PreviewError = "Выберите картинку-шаблон, чтобы увидеть превью.";
            return;
        }

        var template = BuildTemplate();
        var number = (int)Math.Round(SampleNumber);
        var generation = ++_previewGeneration;

        _ = RenderPreviewAsync(template, number, generation);
    }

    private async Task RenderPreviewAsync(CoverTemplate template, int number, int generation)
    {
        try
        {
            var (image, width, height) = await Task.Run(() =>
            {
                using var bitmap = _generator.Render(template, number);
                return (SkiaImaging.ToBitmapSource(bitmap), bitmap.Width, bitmap.Height);
            });

            if (generation != _previewGeneration)
            {
                return;
            }

            PreviewImage = image;
            PreviewError = null;
            SizeWarning = width < 1280 || height < 720
                ? $"Шаблон {width}×{height} меньше 1280×720 – на площадках возможна потеря качества."
                : null;
        }
        catch (Exception ex)
        {
            if (generation != _previewGeneration)
            {
                return;
            }

            PreviewImage = null;
            SizeWarning = null;
            PreviewError = "Не удалось отрисовать превью: " + ex.Message;
        }
    }

    private void AttachLayer(CoverTextLayerViewModel layer) => layer.PropertyChanged += OnLayerChanged;

    private void DetachLayer(CoverTextLayerViewModel layer) => layer.PropertyChanged -= OnLayerChanged;

    private void OnLayerChanged(object? sender, PropertyChangedEventArgs e) => RefreshPreview();

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
