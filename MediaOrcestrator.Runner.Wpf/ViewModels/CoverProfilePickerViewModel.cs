using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class CoverProfilePickerViewModel : ObservableObject
{
    private readonly CoverGenerator _generator;
    private readonly CoverTemplateStore _store;
    private readonly IDialogService _dialogs;
    private readonly IFilePicker _filePicker;
    private readonly ICoverTemplateEditorWindowService _editorWindow;
    private readonly ILogger _logger;

    private bool _suppressSelection;
    private int _previewGeneration;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    private string? _selectedProfileName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTemplate))]
    private CoverTemplate? _selectedTemplate;

    [ObservableProperty]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private string? _previewHint = "Профиль не выбран";

    [ObservableProperty]
    private string? _sampleTitle;

    public CoverProfilePickerViewModel(
        CoverGenerator generator,
        CoverTemplateStore store,
        IDialogService dialogs,
        IFilePicker filePicker,
        ICoverTemplateEditorWindowService editorWindow,
        ILogger logger)
    {
        _generator = generator;
        _store = store;
        _dialogs = dialogs;
        _filePicker = filePicker;
        _editorWindow = editorWindow;
        _logger = logger;

        _store.Changed += OnStoreChanged;
        ReloadProfiles();
    }

    public event EventHandler? TemplateChanged;

    public ObservableCollection<string> Profiles { get; } = [];

    public bool HasTemplate => SelectedTemplate is not null;

    public void ReloadProfiles(string? selectProfileName = null)
    {
        var previous = selectProfileName ?? SelectedProfileName;

        _suppressSelection = true;
        Profiles.Clear();

        foreach (var name in _store.List())
        {
            Profiles.Add(name);
        }

        var target = previous is not null && Profiles.Contains(previous) ? previous : null;
        SelectedProfileName = target;
        _suppressSelection = false;

        SelectedTemplate = string.IsNullOrEmpty(target) ? null : _store.Load(target);
    }

    [RelayCommand]
    private void OpenEditor()
    {
        _editorWindow.Open(SelectedTemplate, SelectedProfileName, SampleTitle, this);
    }

    private void OnStoreChanged(string? savedName)
    {
        var shouldSelect = savedName is not null
                           && (_editorWindow.IsActiveCaller(this)
                               || string.Equals(SelectedProfileName, savedName, StringComparison.OrdinalIgnoreCase));

        ReloadProfiles(shouldSelect ? savedName : null);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private void DeleteProfile()
    {
        if (string.IsNullOrEmpty(SelectedProfileName))
        {
            return;
        }

        var name = SelectedProfileName;

        if (!_dialogs.ConfirmWarning("Удалить профиль",
                $"Удалить профиль обложки «{name}»? Сохранённая копия картинки-шаблона в templates/covers тоже будет удалена."))
        {
            return;
        }

        _store.Delete(name);
        ReloadProfiles();
    }

    private bool CanDeleteProfile() => !string.IsNullOrEmpty(SelectedProfileName);

    partial void OnSelectedProfileNameChanged(string? value)
    {
        if (_suppressSelection)
        {
            return;
        }

        SelectedTemplate = string.IsNullOrEmpty(value) ? null : _store.Load(value);
    }

    partial void OnSelectedTemplateChanged(CoverTemplate? value)
    {
        RefreshPreview();
        TemplateChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSampleTitleChanged(string? value) => RefreshPreview();

    private void RefreshPreview()
    {
        if (SelectedTemplate is not { } template
            || string.IsNullOrEmpty(template.TemplatePath)
            || !File.Exists(template.TemplatePath))
        {
            PreviewImage = null;
            PreviewHint = SelectedTemplate is null ? "Профиль не выбран" : "Картинка-шаблон недоступна";
            return;
        }

        var sampleTitle = SampleTitle;
        var generation = ++_previewGeneration;

        _ = RenderPreviewAsync(template, sampleTitle, generation);
    }

    private async Task RenderPreviewAsync(CoverTemplate template, string? sampleTitle, int generation)
    {
        try
        {
            var image = await Task.Run(() =>
            {
                var number = CoverNumberResolver.Resolve(template, sampleTitle, 0);
                using var bitmap = _generator.Render(template, number);
                return SkiaImaging.ToBitmapSource(bitmap);
            });

            if (generation != _previewGeneration)
            {
                return;
            }

            PreviewImage = image;
            PreviewHint = null;
        }
        catch (Exception ex)
        {
            if (generation != _previewGeneration)
            {
                return;
            }

            PreviewImage = null;
            _logger.LogWarning(ex, "Не удалось отрисовать превью профиля обложки {Profile}", SelectedProfileName);
            PreviewHint = "Не удалось отрисовать превью";
        }
    }
}
