using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class PublishViewModel : ObservableObject, IPageStatus, IPageHeader
{
    public string PageTitle => "Публикация";

    public string? PageDescription =>
        "Загрузка одного видео в выбранный источник. Поля «Название» и «Описание» подскажут варианты из истории и сайдкар-файлов рядом с видео.";


    private const int MaxTitleSuggestions = 20;
    private const int MaxDescriptionTemplates = 10;
    private const int DescriptionPreviewLength = 100;

    private static readonly string[] VideoExtensions =
        [".mp4", ".mkv", ".webm", ".mov", ".avi", ".m4v", ".wmv", ".flv", ".ts"];

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"];

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
        TempManager tempManager)
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

        TemplatePicker = new(coverGenerator, coverTemplateStore, dialogs, filePicker, editorWindow);
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
            var size = TryFormatFileSize(VideoPath);
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
        _ = ReloadSourceSuggestionsAsync();
        SetStatus("Готов к публикации.", false);
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
    private void ClearVideo()
    {
        SetVideo(null);
    }

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
            && !string.Equals(current, template.Text, StringComparison.Ordinal))
        {
            if (!_dialogs.Confirm("Замена описания", "Заменить текущее описание выбранным шаблоном?"))
            {
                return;
            }
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

    [RelayCommand(CanExecute = nameof(CanPublishOrCancel))]
    private async Task PublishAsync()
    {
        if (IsPublishing)
        {
            SetStatus("Отмена…", false);

            if (_publishCts is { } cts)
            {
                await cts.CancelAsync();
            }

            return;
        }

        if (SelectedSource is not { } source)
        {
            SetStatus("Выберите источник.", true);
            return;
        }

        var title = Title.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            SetStatus("Укажите название.", true);
            return;
        }

        if (string.IsNullOrEmpty(VideoPath) || !File.Exists(VideoPath))
        {
            SetStatus("Выберите видеофайл.", true);
            return;
        }

        IsPublishing = true;
        var pubCts = new CancellationTokenSource();
        _publishCts = pubCts;
        var token = pubCts.Token;

        SetStatus($"Публикация в «{source.TitleFull}»…", false);

        var description = Description;
        var videoPath = VideoPath;
        var coverPath = CoverPath;

        var pubAction = _actionHolder.Register(
            $"Публикация: «{title}» ({source.TitleFull})",
            "Публикация…",
            0,
            pubCts,
            kind: ActionKind.Publish);

        try
        {
            Media published;
            using (_actionHolder.BeginScope(pubAction))
            {
                published = await Task.Run(
                    () => _orcestrator.PublishMediaAsync(source, title, description, videoPath, coverPath, token),
                    token);
            }

            pubAction.Finish("Опубликовано");
            SetStatus($"Опубликовано в «{source.TitleFull}».", false);
            _logger.LogInformation("Медиа «{Title}» опубликовано в {Source}", title, source.TitleFull);
            InvalidateMediasCache();

            if (RunChainAfterPublish)
            {
                await RunPostPublishChainAsync(published, source, token);
            }

            ResetForm();
        }
        catch (OperationCanceledException)
        {
            pubAction.MarkCancelled("Публикация отменена");
            _logger.LogInformation("Публикация «{Title}» отменена пользователем", title);
            SetStatus("Публикация отменена.", false);
        }
        catch (Exception exception)
        {
            pubAction.Fail("Ошибка: " + exception.Message, exception);
            _logger.LogError(exception, "Ошибка при публикации «{Title}» в {Source}", title, source.TitleFull);
            SetStatus($"Ошибка: {exception.Message}", true);
        }
        finally
        {
            IsPublishing = false;

            if (ReferenceEquals(_publishCts, pubCts))
            {
                _publishCts = null;
            }

            pubCts.Dispose();
        }
    }

    private bool CanPublishOrCancel()
    {
        if (IsPublishing)
        {
            return true;
        }

        return SelectedSource is not null
               && !string.IsNullOrWhiteSpace(Title)
               && !string.IsNullOrEmpty(VideoPath);
    }

    partial void OnSelectedSourceChanged(Source? value)
    {
        _ = ReloadSourceSuggestionsAsync();
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

        UpdateSidecarDescription(value);
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

        if (_suppressTitleAutoFill)
        {
            return;
        }

        TryAutoFillDescriptionFromTitle(value);
    }

    partial void OnDescriptionChanged(string value)
    {
        DescriptionLength = value?.Length ?? 0;
    }

    private void SetVideo(string? path)
    {
        VideoPath = path;
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    private void ResetForm()
    {
        Title = string.Empty;
        Description = string.Empty;
        SetVideo(null);
        CoverPath = null;
        _ = ReloadSourceSuggestionsAsync();
    }

    private void InvalidateMediasCache()
    {
        _isMediasCacheValid = false;
        _mediasCache.Clear();
    }

    private void TryAutoFillDescriptionFromTitle(string? title)
    {
        if (string.IsNullOrEmpty(title)
            || !string.IsNullOrWhiteSpace(Description)
            || !_titleToDescription.TryGetValue(title, out var description))
        {
            return;
        }

        Description = description;
    }

    private async void UpdateSidecarDescription(string? videoPath)
    {
        try
        {
            await UpdateSidecarDescriptionAsync(videoPath);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось обновить описание из файла рядом с видео {Path}", videoPath);
        }
    }

    private async Task UpdateSidecarDescriptionAsync(string? videoPath)
    {
        _sidecarCts?.Cancel();
        var cts = new CancellationTokenSource();
        _sidecarCts = cts;

        string? sidecar;

        if (string.IsNullOrEmpty(videoPath))
        {
            sidecar = null;
        }
        else
        {
            try
            {
                sidecar = await Task.Run(() => TryReadSidecarDescription(videoPath), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (cts.IsCancellationRequested || !ReferenceEquals(_sidecarCts, cts))
        {
            return;
        }

        _sidecarCts = null;
        cts.Dispose();

        if (string.Equals(_videoSidecarDescription, sidecar, StringComparison.Ordinal))
        {
            return;
        }

        for (var i = DescriptionTemplates.Count - 1; i >= 0; i--)
        {
            if (DescriptionTemplates[i].IsFromVideoFile)
            {
                DescriptionTemplates.RemoveAt(i);
            }
        }

        _videoSidecarDescription = sidecar;

        if (sidecar == null)
        {
            return;
        }

        DescriptionTemplates.Insert(0, new(sidecar, isFromVideoFile: true));

        if (string.IsNullOrWhiteSpace(Description))
        {
            Description = sidecar;
        }
    }

    private string? TryReadSidecarDescription(string videoPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(videoPath);
            var name = Path.GetFileNameWithoutExtension(videoPath);

            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
            {
                return null;
            }

            var descPath = Path.Combine(dir, name + ".description");
            if (File.Exists(descPath))
            {
                var text = File.ReadAllText(descPath).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            var infoPath = Path.Combine(dir, name + ".info.json");
            if (File.Exists(infoPath))
            {
                using var stream = File.OpenRead(infoPath);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("description", out var element)
                    && element.ValueKind == JsonValueKind.String)
                {
                    var text = element.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            var txtPath = Path.Combine(dir, name + ".txt");
            if (File.Exists(txtPath))
            {
                var text = File.ReadAllText(txtPath).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось прочитать описание рядом с видео {Path}", videoPath);
        }

        return null;
    }

    private async Task ReloadSourceSuggestionsAsync()
    {
        _suggestionsCts?.Cancel();
        _suggestionsCts = null;

        ClearSuggestions();

        if (SelectedSource is not { } source)
        {
            return;
        }

        var sourceId = source.Id;

        if (!_isMediasCacheValid)
        {
            var cts = new CancellationTokenSource();
            _suggestionsCts = cts;
            var token = cts.Token;

            try
            {
                var medias = await Task.Run(() => _orcestrator.GetMedias(), token);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                _mediasCache.Clear();
                _mediasCache.AddRange(medias);
                _isMediasCacheValid = true;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Не удалось загрузить историю публикаций для {SourceId}", sourceId);
                return;
            }
            finally
            {
                if (ReferenceEquals(_suggestionsCts, cts))
                {
                    _suggestionsCts = null;
                }

                cts.Dispose();
            }
        }

        if (SelectedSource?.Id != sourceId)
        {
            return;
        }

        PopulateSuggestionsForSource(sourceId);
    }

    private void ClearSuggestions()
    {
        TitleSuggestions.Clear();
        _titleToDescription.Clear();

        for (var i = DescriptionTemplates.Count - 1; i >= 0; i--)
        {
            if (!DescriptionTemplates[i].IsFromVideoFile)
            {
                DescriptionTemplates.RemoveAt(i);
            }
        }
    }

    private void PopulateSuggestionsForSource(string sourceId)
    {
        var links = _mediasCache
            .SelectMany(x => x.Sources ?? [])
            .Where(x => x.SourceId == sourceId)
            .OrderByDescending(x => x.SortNumber)
            .ToList();

        TitleSuggestions.AddRange(links.Select(x => x.Title)
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxTitleSuggestions));

        foreach (var link in links)
        {
            if (string.IsNullOrEmpty(link.Title) || string.IsNullOrWhiteSpace(link.Description))
            {
                continue;
            }

            _titleToDescription.TryAdd(link.Title, link.Description);
        }

        DescriptionTemplates.AddRange(links.Select(x => x.Description)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !string.Equals(x, _videoSidecarDescription, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxDescriptionTemplates)
            .Select(d => new DescriptionTemplateViewModel(d, isFromVideoFile: false)));
    }

    private async Task RunPostPublishChainAsync(Media media, Source publishedSource, CancellationToken cancellationToken)
    {
        var relations = _orcestrator.GetRelations();
        var roots = _syncPlanner.Plan([media], relations);
        var chain = EnumerateChain(roots).ToList();

        if (chain.Count == 0)
        {
            return;
        }

        using var chainCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var chainToken = chainCts.Token;

        var actionName = $"Цепочка после публикации: «{media.Title}» от {publishedSource.TitleFull}";
        var running = _actionHolder.Register(actionName, "В процессе", chain.Count, chainCts, kind: ActionKind.Sync);

        using var chainScope = _actionHolder.BeginScope(running);

        SyncIntent? failedIntent = null;

        try
        {
            for (var i = 0; i < chain.Count; i++)
            {
                chainToken.ThrowIfCancellationRequested();

                var intent = chain[i];
                var step = $"{i + 1}/{chain.Count}";
                var stepLabel = $"{intent.From.TitleFull} → {intent.To.TitleFull}";
                SetStatus($"Цепочка {step}: {stepLabel}…", false);
                _actionHolder.SetStatus(running.Id, $"{step}: {stepLabel}");

                failedIntent = intent;
                await _retryRunner.RunAsync(intent.Media, intent.Relation, cancellationToken: chainToken);
                failedIntent = null;

                _logger.LogInformation("Цепочка: «{Title}» прошла {From} → {To}",
                    intent.Media.Title, intent.From.TitleFull, intent.To.TitleFull);

                running.ProgressPlus();
            }

            SetStatus($"Цепочка завершена ({chain.Count} шаг(ов)).", false);
            running.Finish();
        }
        catch (OperationCanceledException)
        {
            running.MarkCancelled();
            throw;
        }
        catch (Exception exception) when (failedIntent is not null)
        {
            _logger.LogError(exception, "Цепочка: «{Title}» сорвалась на {From} → {To}",
                media.Title, failedIntent.From.TitleFull, failedIntent.To.TitleFull);

            SetStatus($"Цепочка прервана на {failedIntent.To.TitleFull}: {exception.Message}", true);
            running.Fail($"Ошибка на {failedIntent.To.TitleFull}", exception);
        }
    }

    private static IEnumerable<SyncIntent> EnumerateChain(List<SyncIntent> intents)
    {
        foreach (var intent in intents.Where(x => x.IsSelected))
        {
            yield return intent;

            foreach (var child in EnumerateChain(intent.NextIntents))
            {
                yield return child;
            }
        }
    }

    private static string? TryFormatFileSize(string path)
    {
        try
        {
            return FormatFileSize(new FileInfo(path).Length);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ"];
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:F1} {units[unitIndex]}";
    }
}

public sealed class DescriptionTemplateViewModel(string text, bool isFromVideoFile)
{
    private const int PreviewLength = 100;

    public string Text { get; } = text;

    public bool IsFromVideoFile { get; } = isFromVideoFile;

    public string Preview
    {
        get
        {
            var newLine = Text.IndexOfAny(['\r', '\n']);
            var firstLine = newLine >= 0 ? Text[..newLine] : Text;
            firstLine = firstLine.Trim();

            var truncated = firstLine.Length > PreviewLength
                ? firstLine[..PreviewLength] + "…"
                : firstLine;

            return IsFromVideoFile ? "[Из файла видео] " + truncated : truncated;
        }
    }

    public override string ToString() => Preview;
}
