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

    [ObservableProperty]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private string? _previewHint;

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
    private void SetSourceMode(BatchPreviewSourceMode mode)
    {
        SourceMode = mode;
    }

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

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var requests = BuildRequests();

        if (requests.Count == 0)
        {
            return;
        }

        var donor = SourceMode == BatchPreviewSourceMode.Donor ? SelectedDonor : null;
        var localFilePath = SourceMode == BatchPreviewSourceMode.File ? FilePath : null;
        var coverTemplate = SourceMode == BatchPreviewSourceMode.Template ? TemplatePicker.SelectedTemplate : null;

        var totalUnits = requests.Sum(r => r.Targets.Count);

        _applyCts?.Dispose();
        _applyCts = new();
        var token = _applyCts.Token;

        IsApplying = true;
        ProgressMax = Math.Max(totalUnits, 1);
        ProgressValue = 0;
        StatusText = "Подготовка";

        AppendJournal(OperationJournalLevel.Info,
            $"── Запуск: видео {requests.Count}, заливок {totalUnits} ──");

        var actionName = requests.Count == 1
            ? $"Обновление превью: «{Truncate(requests[0].Media.Title, 50)}»"
            : $"Обновление превью ({requests.Count} видео)";

        var running = _actionHolder.Register(actionName, "Подготовка", totalUnits, _applyCts, kind: ActionKind.Metadata);
        using var scope = _actionHolder.BeginScope(running);

        try
        {
            running.Status = "Авторизация площадок";

            if (!await EnsureAuthenticatedAsync(donor, requests, token))
            {
                StatusText = "Отменено – нет авторизации";
                AppendJournal(OperationJournalLevel.Warning, "Применение отменено: площадки не авторизованы");
                running.MarkCancelled("Нет авторизации площадок");
                return;
            }

            running.Status = $"0/{totalUnits}";

            var progress = new Progress<BatchPreviewProgress>(p => OnProgress(p, totalUnits, running));
            var uiContext = SynchronizationContext.Current ?? new();
            var processed = 0;

            var execOptions = new BatchPreviewExecutionOptions(StopOnError);

            var results = await Task.Run(() => _service.ApplyAsync(requests,
                    donor,
                    localFilePath,
                    coverTemplate,
                    progress,
                    result =>
                    {
                        Interlocked.Increment(ref processed);
                        var snapshot = processed;
                        uiContext.Post(_ => OnResultReported(result, snapshot, totalUnits, running), null);
                    },
                    token,
                    execOptions),
                token);

            var success = results.Count(r => r.Success);
            var failed = results.Count - success;

            StatusText = failed == 0
                ? $"Готово: применено {success} из {results.Count}"
                : $"Готово: {success} успешно, {failed} с ошибками";

            AppendJournal(failed == 0 ? OperationJournalLevel.Success : OperationJournalLevel.Error,
                $"── Готово: успешно {success}, с ошибками {failed} ──");

            if (failed == 0)
            {
                running.Finish($"Применено {success} из {results.Count}");
            }
            else
            {
                running.Fail($"Применено {success}, ошибок {failed}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Отменено пользователем";
            AppendJournal(OperationJournalLevel.Warning, "── Прервано пользователем ──");
            running.MarkCancelled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Пакетное обновление превью сорвалось");
            StatusText = "Ошибка: " + ex.Message;
            AppendJournal(OperationJournalLevel.Error, "Ошибка: " + ex.Message);
            running.Fail(ex.Message, ex);
        }
        finally
        {
            IsApplying = false;
            ProgressValue = ProgressMax;
        }
    }

    [RelayCommand(CanExecute = nameof(IsApplyingValue))]
    private void Cancel()
    {
        try
        {
            _applyCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool IsApplyingValue() => IsApplying;

    partial void OnIsApplyingChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditParameters));
    }

    partial void OnSourceModeChanged(BatchPreviewSourceMode value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(SourceModeKey, value.ToString());
        }

        RebuildTargets();
        RebuildRows();
        RefreshPreviewTile();
    }

    partial void OnSelectedDonorChanged(Source? value)
    {
        RebuildTargets();
        RebuildRows();
        RefreshPreviewTile();
    }

    partial void OnFilePathChanged(string? value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(LastFilePathKey, value ?? string.Empty);
        }

        OnPropertyChanged(nameof(HasFilePath));
        ApplyCommand.NotifyCanExecuteChanged();
        RefreshPreviewTile();
    }

    partial void OnStopOnErrorChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(StopOnErrorKey, value ? "true" : "false");
        }
    }

    partial void OnIsCompactChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(CompactKey, value ? "true" : "false");
        }
    }

    partial void OnActionableCountChanged(int value)
    {
        OnPropertyChanged(nameof(ApplyButtonText));
    }

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

    private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressEvents || e.PropertyName != nameof(SourceCheckOptionViewModel.IsChecked))
        {
            return;
        }

        RebuildRows();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressEvents || e.PropertyName != nameof(BatchPreviewRowViewModel.IsApplyChecked))
        {
            return;
        }

        RecountActionable();
    }

    private void RebuildDonors()
    {
        var previousId = SelectedDonor?.Id;
        Donors.ReplaceAll(_service.GetAvailableDonors(_medias));

        SelectedDonor = previousId is not null
            ? Donors.FirstOrDefault(d => d.Id == previousId) ?? Donors.FirstOrDefault()
            : Donors.FirstOrDefault();
    }

    private void RebuildTargets()
    {
        _suppressEvents = true;
        try
        {
            var previousChecks = Targets.ToDictionary(s => s.SourceId, s => s.IsChecked, StringComparer.Ordinal);

            foreach (var target in Targets)
            {
                target.PropertyChanged -= OnTargetPropertyChanged;
            }

            var donor = SourceMode == BatchPreviewSourceMode.Donor ? SelectedDonor : null;
            var newTargets = _service.GetAvailableTargets(_medias, donor)
                .Select(target => new SourceCheckOptionViewModel
                {
                    SourceId = target.Id,
                    Title = target.TitleFull,
                    IsChecked = !previousChecks.TryGetValue(target.Id, out var was) || was,
                })
                .ToList();

            foreach (var target in newTargets)
            {
                target.PropertyChanged += OnTargetPropertyChanged;
            }

            Targets.ReplaceAll(newTargets);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void RebuildRows()
    {
        _suppressEvents = true;
        try
        {
            foreach (var row in Rows)
            {
                row.PropertyChanged -= OnRowPropertyChanged;
            }

            var donor = SourceMode == BatchPreviewSourceMode.Donor ? SelectedDonor : null;
            var available = _service.GetAvailableTargets(_medias, donor);
            var checkedIds = Targets
                .Where(t => t.IsChecked)
                .Select(t => t.SourceId)
                .ToHashSet(StringComparer.Ordinal);

            var selectedTargets = available.Where(t => checkedIds.Contains(t.Id)).ToList();
            var newRows = new List<BatchPreviewRowViewModel>();

            foreach (var media in _medias)
            {
                foreach (var target in selectedTargets)
                {
                    if (media.Sources.All(s => s.SourceId != target.Id))
                    {
                        continue;
                    }

                    newRows.Add(new()
                    {
                        Media = media,
                        Target = target,
                    });
                }
            }

            foreach (var row in newRows)
            {
                row.PropertyChanged += OnRowPropertyChanged;
            }

            Rows.ReplaceAll(newRows);
        }
        finally
        {
            _suppressEvents = false;
        }

        RecountActionable();
    }

    private void RecountActionable()
    {
        ActionableCount = Rows.Count(r => r.IsApplyChecked && !r.IsApplied);
    }

    private bool CanApply()
    {
        if (IsApplying || ActionableCount == 0)
        {
            return false;
        }

        return SourceMode switch
        {
            BatchPreviewSourceMode.Donor => SelectedDonor is not null,
            BatchPreviewSourceMode.File => HasFilePath,
            BatchPreviewSourceMode.Template => TemplatePicker.SelectedTemplate is not null,
            _ => false,
        };
    }

    private List<BatchPreviewRequest> BuildRequests()
    {
        var byMedia = new Dictionary<string, (Media Media, List<Source> Targets)>(StringComparer.Ordinal);

        foreach (var row in Rows)
        {
            if (!row.IsApplyChecked || row.IsApplied)
            {
                continue;
            }

            if (!byMedia.TryGetValue(row.Media.Id, out var bucket))
            {
                bucket = (row.Media, []);
                byMedia[row.Media.Id] = bucket;
            }

            bucket.Targets.Add(row.Target);
        }

        var requests = new List<BatchPreviewRequest>(byMedia.Count);
        foreach (var media in _medias)
        {
            if (byMedia.TryGetValue(media.Id, out var bucket))
            {
                requests.Add(new(bucket.Media, bucket.Targets));
            }
        }

        return requests;
    }

    private async Task<bool> EnsureAuthenticatedAsync(Source? donor, IReadOnlyList<BatchPreviewRequest> requests, CancellationToken token)
    {
        var needed = new HashSet<string>(StringComparer.Ordinal);

        if (donor is not null)
        {
            needed.Add(donor.Id);
        }

        foreach (var target in requests.SelectMany(r => r.Targets))
        {
            needed.Add(target.Id);
        }

        if (needed.Count == 0)
        {
            return true;
        }

        var pending = _service.GetUnauthenticatedSources(needed);

        if (pending.Count == 0)
        {
            return true;
        }

        var message = "Перед обновлением превью нужно войти на площадки:" + Environment.NewLine
            + string.Join(Environment.NewLine, pending.Select(p => "• " + p.Title)) + Environment.NewLine
            + Environment.NewLine
            + "В WPF-версии auth-flow ещё не подключён. Войдите через WinForms-Runner и попробуйте снова.";

        _dialogs.Warning("Требуется авторизация", message);

        token.ThrowIfCancellationRequested();
        return false;
    }

    private void OnProgress(BatchPreviewProgress progress, int totalUnits, ActionHolder.RunningAction running)
    {
        var current = string.IsNullOrEmpty(progress.CurrentTitle) ? "..." : Truncate(progress.CurrentTitle, 50);

        if (progress.Processed < progress.Total)
        {
            StatusText = $"Обработка {progress.Processed + 1}/{progress.Total}: {current}";
        }

        if (running.State == ActionState.Running)
        {
            running.Subtitle = current;
        }
    }

    private void OnResultReported(BatchPreviewResult result, int processed, int totalUnits, ActionHolder.RunningAction running)
    {
        var row = Rows.FirstOrDefault(r =>
            r.Media.Id == result.Media.Id && r.Target.Id == result.Target.Id);

        if (row is not null)
        {
            row.IsApplied = true;
            row.IsApplyError = !result.Success;
            row.StatusText = result.Success ? "Готово" : result.ErrorMessage ?? "Ошибка";
            row.ResultTooltip = result.Success ? null : result.ErrorMessage;
        }

        var title = Truncate(result.Media.Title, 50);
        var targetTitle = result.Target.TitleFull;

        if (result.Success)
        {
            AppendJournal(OperationJournalLevel.Success, $"✓ «{title}» → {targetTitle}");
        }
        else
        {
            AppendJournal(OperationJournalLevel.Error,
                $"✗ «{title}» → {targetTitle} – {result.ErrorMessage ?? "ошибка"}");
        }

        ProgressMax = Math.Max(totalUnits, 1);
        ProgressValue = Math.Min(processed, ProgressMax);

        if (running.State == ActionState.Running)
        {
            running.ProgressMax = totalUnits;
            running.SetProgress(Math.Min(processed, totalUnits));
            running.Status = $"{Math.Min(processed, totalUnits)}/{totalUnits}";
        }
    }

    private void SetAllTargets(bool value)
    {
        _suppressEvents = true;
        try
        {
            foreach (var target in Targets)
            {
                target.IsChecked = value;
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        RebuildRows();
    }

    private void SetAllRowChecks(bool value)
    {
        _suppressEvents = true;
        try
        {
            foreach (var row in Rows)
            {
                row.IsApplyChecked = value;
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        RecountActionable();
    }

    private void AppendJournal(OperationJournalLevel level, string text, int indent = 0)
    {
        Journal.Add(new(DateTime.Now, level, text, indent));

        while (Journal.Count > JournalMaxEntries)
        {
            Journal.RemoveAt(0);
        }
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value ?? string.Empty;
        }

        return value.AsSpan(0, max - 1).ToString() + "…";
    }
}
