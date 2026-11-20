using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class PublishViewModel : ObservableObject, IPageStatus, IPageHeader
{
    public string PageTitle => "Публикация";

    public string? PageDescription =>
        "Загрузка одного видео в выбранный источник. Поля «Название» и «Описание» подскажут варианты из истории и сайдкар-файлов рядом с видео.";

    private static readonly string[] VideoExtensions =
        [".mp4", ".mkv", ".webm", ".mov", ".avi", ".m4v", ".wmv", ".flv", ".ts"];

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".bmp"];

    private readonly Orcestrator _orcestrator;
    private readonly SyncRetryRunner _retryRunner;
    private readonly SyncPlanner _syncPlanner;
    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly ILogger<PublishViewModel> _logger;
    private readonly IFilePicker _filePicker;
    private readonly CoverGenerator _coverGenerator;
    private readonly TempManager _tempManager;

    private readonly List<Media> _mediasCache = [];
    private readonly Dictionary<string, string> _titleToDescription = new(StringComparer.Ordinal);
    private CancellationTokenSource? _publishCts;
    private CancellationTokenSource? _suggestionsCts;
    private CancellationTokenSource? _sidecarCts;
    private bool _isMediasCacheValid;
    private bool _suppressTitleAutoFill;
    private string? _videoSidecarDescription;
    private string? _generatedCoverDir;

    public PublishViewModel(
        Orcestrator orcestrator,
        SyncRetryRunner retryRunner,
        SyncPlanner syncPlanner,
        ActionHolder actionHolder,
        IDialogService dialogs,
        ILogger<PublishViewModel> logger,
        IFilePicker filePicker,
        CoverGenerator coverGenerator,
        CoverTemplateStore coverTemplateStore,
        ICoverTemplateEditorWindowService editorWindow,
        TempManager tempManager,
        INavigationService? navigation = null)
    {
        _orcestrator = orcestrator;
        _retryRunner = retryRunner;
        _syncPlanner = syncPlanner;
        _actionHolder = actionHolder;
        _dialogs = dialogs;
        _logger = logger;
        _filePicker = filePicker;
        _coverGenerator = coverGenerator;
        _tempManager = tempManager;
        AuthenticationRecovery = new(navigation);

        TemplatePicker = new(coverGenerator, coverTemplateStore, dialogs, filePicker, editorWindow, logger);
        TemplatePicker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CoverProfilePickerViewModel.HasTemplate) or nameof(CoverProfilePickerViewModel.SelectedTemplate))
            {
                GenerateCoverFromTemplateCommand.NotifyCanExecuteChanged();
            }
        };

        TitleSuggestions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTitleSuggestions));
            OnPropertyChanged(nameof(FirstTitleSuggestion));
        };
        DescriptionTemplates.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDescriptionTemplates));
    }

    public CoverProfilePickerViewModel TemplatePicker { get; }
    public SourceAuthenticationRecoveryViewModel AuthenticationRecovery { get; }

    internal Task? PendingSidecarTask { get; private set; }

    internal Task? PendingSuggestionsTask { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditFields))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseVideoCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCoverCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCoverFromTemplateCommand))]
    private bool _isPublishing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private Source? _selectedSource;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearVideoCommand))]
    [NotifyPropertyChangedFor(nameof(VideoLabel))]
    [NotifyPropertyChangedFor(nameof(HasVideo))]
    private string? _videoPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCoverCommand))]
    [NotifyPropertyChangedFor(nameof(CoverLabel))]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private string? _coverPath;

    [ObservableProperty]
    private bool _runChainAfterPublish;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    private string _statusMessage = "Готов к публикации.";

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private int _descriptionLength;

    public RangeObservableCollection<Source> Sources { get; } = [];

    public RangeObservableCollection<string> TitleSuggestions { get; } = [];

    public RangeObservableCollection<DescriptionTemplateViewModel> DescriptionTemplates { get; } = [];

    public bool CanEditFields => !IsPublishing;

    public bool HasVideo => !string.IsNullOrEmpty(VideoPath);

    public bool HasCover => !string.IsNullOrEmpty(CoverPath);

    public bool HasTitleSuggestions => TitleSuggestions.Count > 0;

    public string? FirstTitleSuggestion => TitleSuggestions.Count > 0 ? TitleSuggestions[0] : null;

    public bool HasDescriptionTemplates => DescriptionTemplates.Count > 0;

    public string VideoLabel
    {
        get
        {
            if (string.IsNullOrEmpty(VideoPath))
            {
                return string.Empty;
            }

            var name = Path.GetFileName(VideoPath);
            var size = PublishSuggestionHelper.TryFormatFileSize(VideoPath);
            return size != null ? $"{name} · {size}" : name;
        }
    }

    public string CoverLabel => string.IsNullOrEmpty(CoverPath) ? string.Empty : Path.GetFileName(CoverPath);

    public bool IsBusy => IsPublishing;

    public bool IsIndeterminate => true;

    public double ProgressValue => 0;

    public double ProgressMax => 1;

    public string? StatusCaption => StatusMessage;

    public ICommand? CancelCommand => PublishCommand;

    public void Reload()
    {
        var previousId = SelectedSource?.Id;

        Sources.ReplaceAll(_orcestrator.GetSources()
            .Where(x => x is { IsDisable: false, Type: not null } && x.Type.ChannelType != SyncDirection.OnlyDownload));

        if (previousId is not null)
        {
            SelectedSource = Sources.FirstOrDefault(x => x.Id == previousId) ?? Sources.FirstOrDefault();
        }
        else
        {
            SelectedSource ??= Sources.FirstOrDefault();
        }

        InvalidateMediasCache();
        PendingSuggestionsTask = ReloadSourceSuggestionsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanEditFields))]
    private void BrowseVideo()
    {
        var request = new FileOpenRequest(
            "Выберите видеофайл",
            "Видео|" + string.Join(";", VideoExtensions.Select(e => "*" + e)) + "|Все файлы|*.*");

        var path = _filePicker.PickFile(request);

        if (path is not null)
        {
            SetVideo(path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditFields))]
    private void BrowseCover()
    {
        var request = new FileOpenRequest(
            "Выберите обложку",
            "Изображения|" + string.Join(";", ImageExtensions.Select(e => "*" + e)) + "|Все файлы|*.*");

        var path = _filePicker.PickFile(request);

        if (path is not null)
        {
            CoverPath = path;
        }
    }

    [RelayCommand(CanExecute = nameof(HasVideo))]
    private void ClearVideo() => SetVideo(null);

    [RelayCommand(CanExecute = nameof(HasCover))]
    private void ClearCover()
    {
        CoverPath = null;
        CleanupGeneratedCoverDir();
    }

    [RelayCommand(CanExecute = nameof(CanGenerateCoverFromTemplate))]
    private async Task GenerateCoverFromTemplateAsync()
    {
        if (TemplatePicker.SelectedTemplate is not { } template)
        {
            return;
        }

        if (string.IsNullOrEmpty(template.TemplatePath) || !File.Exists(template.TemplatePath))
        {
            SetStatus("Картинка выбранного шаблона недоступна.", true);
            return;
        }

        try
        {
            var number = CoverNumberResolver.Resolve(template, Title, 0);
            var outputDir = Path.Combine(_tempManager.TempPath, "publish-covers", Guid.NewGuid().ToString("N"));
            var outputPath = await Task.Run(() => _coverGenerator.Generate(template, number, outputDir));

            CleanupGeneratedCoverDir();
            _generatedCoverDir = outputDir;
            CoverPath = outputPath;
            SetStatus($"Обложка по шаблону «{TemplatePicker.SelectedProfileName}» (№{number}) готова.", false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось сгенерировать обложку по шаблону");
            SetStatus("Ошибка генерации обложки: " + ex.Message, true);
        }
    }

    private bool CanGenerateCoverFromTemplate() => CanEditFields && TemplatePicker.HasTemplate;

    private void CleanupGeneratedCoverDir()
    {
        if (string.IsNullOrEmpty(_generatedCoverDir))
        {
            return;
        }

        try
        {
            if (Directory.Exists(_generatedCoverDir))
            {
                Directory.Delete(_generatedCoverDir, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Не удалось удалить временную папку обложки {Dir}", _generatedCoverDir);
        }

        _generatedCoverDir = null;
    }

    [RelayCommand]
    private void ApplyDescriptionTemplate(DescriptionTemplateViewModel? template)
    {
        if (template is null)
        {
            return;
        }

        var current = Description;

        if (!string.IsNullOrWhiteSpace(current)
            && !string.Equals(current, template.Text, StringComparison.Ordinal)
            && !_dialogs.Confirm("Замена описания", "Заменить текущее описание выбранным шаблоном?"))
        {
            return;
        }

        Description = template.Text;
    }

    [RelayCommand]
    private void ApplyTitleSuggestion(string? suggestion)
    {
        if (string.IsNullOrEmpty(suggestion))
        {
            return;
        }

        Title = suggestion;
        TryAutoFillDescriptionFromTitle(suggestion);
    }

    partial void OnSelectedSourceChanged(Source? value)
    {
        PendingSuggestionsTask = ReloadSourceSuggestionsAsync();
    }

    partial void OnVideoPathChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && string.IsNullOrWhiteSpace(Title))
        {
            _suppressTitleAutoFill = true;
            try
            {
                Title = Path.GetFileNameWithoutExtension(value);
            }
            finally
            {
                _suppressTitleAutoFill = false;
            }

            TryAutoFillDescriptionFromTitle(Title);
        }

        PendingSidecarTask = UpdateSidecarDescriptionSafeAsync(value);
    }

    partial void OnCoverPathChanged(string? value)
    {
        if (string.IsNullOrEmpty(_generatedCoverDir))
        {
            return;
        }

        if (string.IsNullOrEmpty(value)
            || !value.StartsWith(_generatedCoverDir, StringComparison.OrdinalIgnoreCase))
        {
            CleanupGeneratedCoverDir();
        }
    }

    partial void OnTitleChanged(string value)
    {
        TemplatePicker.SampleTitle = value;

        if (!_suppressTitleAutoFill)
        {
            TryAutoFillDescriptionFromTitle(value);
        }
    }

    partial void OnDescriptionChanged(string value)
    {
        DescriptionLength = value?.Length ?? 0;
    }

    private void SetVideo(string? path) => VideoPath = path;

    private void ResetForm()
    {
        Title = string.Empty;
        Description = string.Empty;
        SetVideo(null);
        CoverPath = null;
        PendingSuggestionsTask = ReloadSourceSuggestionsAsync();
    }
}
