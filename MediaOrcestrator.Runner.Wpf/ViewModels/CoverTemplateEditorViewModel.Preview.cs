using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Services;
using System.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class CoverTemplateEditorViewModel
{
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

    private void OnPreviewTimerTick()
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
}
