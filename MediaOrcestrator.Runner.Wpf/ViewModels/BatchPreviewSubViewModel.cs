using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class BatchPreviewSubViewModel : ObservableObject
{
    private const string StopOnErrorKey = "wpf.operations.preview.stop_on_error";
    private const string SourceModeKey = "wpf.operations.preview.source_mode";
    private const string LastFilePathKey = "wpf.operations.preview.last_file_path";
    private const string TemplateProfileKey = "wpf.operations.preview.template_profile";
    private const string CompactKey = "wpf.operations.preview.compact";
    private const int JournalMaxEntries = 500;

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".bmp"];

    private readonly Orcestrator _orcestrator;
    private readonly BatchPreviewService _service;
    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly ILogger<BatchPreviewSubViewModel> _logger;
    private readonly IFilePicker _filePicker;

    private readonly List<Media> _medias = [];

    private bool _suppressEvents;
    private bool _suppressPersist;
    private CancellationTokenSource? _applyCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedias))]
    [NotifyPropertyChangedFor(nameof(IsSetEmpty))]
    [NotifyPropertyChangedFor(nameof(MediasSummary))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private int _mediasCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private int _actionableCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDonorMode))]
    [NotifyPropertyChangedFor(nameof(IsFileMode))]
    [NotifyPropertyChangedFor(nameof(IsTemplateMode))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private BatchPreviewSourceMode _sourceMode = BatchPreviewSourceMode.Donor;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private Source? _selectedDonor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilePath))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearFileCommand))]
    private string? _filePath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isApplying;

    [ObservableProperty]
    private bool _stopOnError = true;

    [ObservableProperty]
    private string _statusText = "Готов";

    [ObservableProperty]
    private bool _isJournalOpen;

    [ObservableProperty]
    private bool _isCompact;

    [ObservableProperty]
    private int _progressMax = 1;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private string? _previewHint;

    public BatchPreviewSubViewModel(
        Orcestrator orcestrator,
        BatchPreviewService service,
        ActionHolder actionHolder,
        IDialogService dialogs,
        ISettingsStore settings,
        CoverGenerator coverGenerator,
        CoverTemplateStore coverTemplateStore,
        ICoverTemplateEditorWindowService editorWindow,
        ILogger<BatchPreviewSubViewModel> logger,
        IFilePicker filePicker)
    {
        _orcestrator = orcestrator;
        _service = service;
        _actionHolder = actionHolder;
        _dialogs = dialogs;
        _settings = settings;
        _logger = logger;
        _filePicker = filePicker;

        TemplatePicker = new(coverGenerator, coverTemplateStore, dialogs, filePicker, editorWindow);
        TemplatePicker.TemplateChanged += OnTemplateChanged;
        TemplatePicker.PropertyChanged += OnTemplatePickerPropertyChanged;
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsResultEmpty));

        LoadPreferences();
        RefreshPreviewTile();
    }

    public CoverProfilePickerViewModel TemplatePicker { get; }

    public RangeObservableCollection<Source> Donors { get; } = [];

    public RangeObservableCollection<SourceCheckOptionViewModel> Targets { get; } = [];

    public RangeObservableCollection<BatchPreviewRowViewModel> Rows { get; } = [];

    public RangeObservableCollection<OperationJournalEntry> Journal { get; } = [];

    public bool HasMedias => _medias.Count > 0;

    public bool IsSetEmpty => !HasMedias;

    public bool IsResultEmpty => Rows.Count == 0;

    public bool HasFilePath => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);

    public bool IsDonorMode => SourceMode == BatchPreviewSourceMode.Donor;

    public bool IsFileMode => SourceMode == BatchPreviewSourceMode.File;

    public bool IsTemplateMode => SourceMode == BatchPreviewSourceMode.Template;

    public bool CanEditParameters => !IsApplying;

    public string MediasSummary => _medias.Count switch
    {
        0 => "Набор пуст",
        1 => "Набор: 1 видео",
        _ => $"Набор: {_medias.Count} видео",
    };

    public string ApplyButtonText => ActionableCount > 0
        ? $"Применить ({ActionableCount})"
        : "Применить";

    public void LoadMedia(IReadOnlyList<Media> medias, bool append = false)
    {
        if (!append)
        {
            _medias.Clear();
        }

        var seen = new HashSet<string>(_medias.Select(m => m.Id), StringComparer.Ordinal);

        foreach (var media in medias)
        {
            if (seen.Add(media.Id))
            {
                _medias.Add(media);
            }
        }

        MediasCount = _medias.Count;
        RebuildDonors();
        RebuildTargets();
        RebuildRows();
        TemplatePicker.SampleTitle = _medias.FirstOrDefault()?.Title;
    }

    [RelayCommand]
    private void SetSourceMode(BatchPreviewSourceMode mode) => SourceMode = mode;

    [RelayCommand]
    private async Task LoadAllMediaAsync()
    {
        var all = await Task.Run(() => _orcestrator.GetMedias());
        LoadMedia(all, append: false);
        _logger.LogInformation("Обложки и превью: загружено всё хранилище – {Count} медиа", all.Count);
    }

    [RelayCommand]
    private void ClearMedia()
    {
        _medias.Clear();
        MediasCount = 0;

        foreach (var target in Targets)
        {
            target.PropertyChanged -= OnTargetPropertyChanged;
        }

        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        Donors.ReplaceAll([]);
        Targets.ReplaceAll([]);
        Rows.ReplaceAll([]);
        SelectedDonor = null;
        TemplatePicker.SampleTitle = null;
        RecountActionable();
    }

    [RelayCommand]
    private void BrowseFile()
    {
        var request = new FileOpenRequest(
            "Выберите файл превью",
            "Изображения|" + string.Join(";", ImageExtensions.Select(e => "*" + e)) + "|Все файлы|*.*");

        var path = _filePicker.PickFile(request);

        if (path is not null)
        {
            FilePath = path;
        }
    }

    [RelayCommand(CanExecute = nameof(HasAnyFilePath))]
    private void ClearFile() => FilePath = null;

    private bool HasAnyFilePath() => !string.IsNullOrEmpty(FilePath);

    [RelayCommand]
    private void SelectAllTargets() => SetAllTargets(true);

    [RelayCommand]
    private void SelectNoTargets() => SetAllTargets(false);

    [RelayCommand]
    private void SelectAllRows() => SetAllRowChecks(true);

    [RelayCommand]
    private void SelectNoRows() => SetAllRowChecks(false);

    [RelayCommand]
    private void ToggleJournal() => IsJournalOpen = !IsJournalOpen;

    [RelayCommand]
    private void ClearJournal() => Journal.Clear();

    private void OnTemplateChanged(object? sender, EventArgs e)
    {
        ApplyCommand.NotifyCanExecuteChanged();
        RefreshPreviewTile();
    }

    private void OnTemplatePickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CoverProfilePickerViewModel.PreviewImage)
            or nameof(CoverProfilePickerViewModel.PreviewHint))
        {
            if (IsTemplateMode)
            {
                RefreshPreviewTile();
            }

            return;
        }

        if (_suppressPersist || e.PropertyName != nameof(CoverProfilePickerViewModel.SelectedProfileName))
        {
            return;
        }

        _settings.SetValue(TemplateProfileKey, TemplatePicker.SelectedProfileName ?? string.Empty);
    }

    private void RefreshPreviewTile()
    {
        switch (SourceMode)
        {
            case BatchPreviewSourceMode.Template:
                PreviewImage = TemplatePicker.PreviewImage;
                PreviewHint = TemplatePicker.PreviewImage is null
                    ? TemplatePicker.PreviewHint ?? "Выберите профиль шаблона"
                    : null;
                break;

            case BatchPreviewSourceMode.File:
                PreviewImage = TryLoadImage(FilePath);
                PreviewHint = PreviewImage is null
                    ? HasFilePath ? "Не удалось загрузить изображение" : "Выберите файл превью"
                    : null;
                break;

            default:
                PreviewImage = null;
                PreviewHint = "Превью берётся с площадки-донора для каждого видео";
                break;
        }
    }

    private static ImageSource? TryLoadImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 320;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void LoadPreferences()
    {
        _suppressPersist = true;
        try
        {
            if (Enum.TryParse<BatchPreviewSourceMode>(_settings.GetStringValue(SourceModeKey), out var mode))
            {
                SourceMode = mode;
            }

            if (bool.TryParse(_settings.GetStringValue(StopOnErrorKey), out var stop))
            {
                StopOnError = stop;
            }

            if (bool.TryParse(_settings.GetStringValue(CompactKey), out var compact))
            {
                IsCompact = compact;
            }

            var lastPath = _settings.GetStringValue(LastFilePathKey);
            if (!string.IsNullOrEmpty(lastPath) && File.Exists(lastPath))
            {
                FilePath = lastPath;
            }

            var templateProfile = _settings.GetStringValue(TemplateProfileKey);
            if (!string.IsNullOrEmpty(templateProfile) && TemplatePicker.Profiles.Contains(templateProfile))
            {
                TemplatePicker.SelectedProfileName = templateProfile;
            }
        }
        finally
        {
            _suppressPersist = false;
        }
    }
}
