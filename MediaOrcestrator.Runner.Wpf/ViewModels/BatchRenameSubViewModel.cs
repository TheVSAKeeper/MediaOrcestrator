using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class BatchRenameSubViewModel : ObservableObject
{
    private const string IgnoreCaseKey = "wpf.operations.rename.ignore_case";
    private const string ModeKey = "wpf.operations.rename.mode";
    private const string StopOnErrorKey = "wpf.operations.rename.stop_on_error";
    private const string CompactKey = "wpf.operations.rename.compact";
    private const int PreviewDebounceMs = 250;
    private const int JournalMaxEntries = 500;

    private readonly Orcestrator _orcestrator;
    private readonly BatchRenameService _service;
    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<BatchRenameSubViewModel> _logger;

    private readonly IUiTimer _previewTimer;
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
    private string _find = string.Empty;

    [ObservableProperty]
    private string _replace = string.Empty;

    [ObservableProperty]
    private BatchRenameMode _mode = BatchRenameMode.Plain;

    [ObservableProperty]
    private bool _ignoreCase;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isApplying;

    [ObservableProperty]
    private bool _stopOnError = true;

    [ObservableProperty]
    private string? _regexError;

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

    public BatchRenameSubViewModel(
        Orcestrator orcestrator,
        BatchRenameService service,
        ActionHolder actionHolder,
        IDialogService dialogs,
        ISettingsStore settings,
        IUiDispatcher dispatcher,
        ILogger<BatchRenameSubViewModel> logger)
    {
        _orcestrator = orcestrator;
        _service = service;
        _actionHolder = actionHolder;
        _dialogs = dialogs;
        _settings = settings;
        _dispatcher = dispatcher;
        _logger = logger;

        _previewTimer = dispatcher.CreateTimer(TimeSpan.FromMilliseconds(PreviewDebounceMs), OnPreviewTimerTick);
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsResultEmpty));

        LoadPreferences();
    }

    public IReadOnlyList<BatchRenameModeOption> ModeOptions { get; } =
    [
        new(BatchRenameMode.Plain, "Обычная замена"),
        new(BatchRenameMode.Regex, "Регулярное выражение"),
    ];

    public RangeObservableCollection<SourceCheckOptionViewModel> Sources { get; } = [];

    public RangeObservableCollection<BatchRenameRowViewModel> Rows { get; } = [];

    public RangeObservableCollection<OperationJournalEntry> Journal { get; } = [];

    public bool HasMedias => _medias.Count > 0;

    public bool IsSetEmpty => !HasMedias;

    public bool IsResultEmpty => Rows.Count == 0;

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

        RebuildSources();
        RebuildRows();

        SchedulePreview();
    }

    [RelayCommand]
    private async Task LoadAllMediaAsync()
    {
        var all = await Task.Run(() => _orcestrator.GetMedias());
        LoadMedia(all, append: false);
        _logger.LogInformation("Переименование: загружено всё хранилище – {Count} медиа", all.Count);
    }

    [RelayCommand]
    private void ClearMedia()
    {
        _medias.Clear();
        MediasCount = 0;

        foreach (var option in Sources)
        {
            option.PropertyChanged -= OnSourceOptionPropertyChanged;
        }

        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        Sources.ReplaceAll([]);
        Rows.ReplaceAll([]);
        RegexError = null;
        RecountActionable();
    }

    [RelayCommand]
    private void SelectAllSources() => SetAllSources(true);

    [RelayCommand]
    private void SelectNoSources() => SetAllSources(false);

    [RelayCommand]
    private void SelectAllRows() => SetAllRowChecks(true);

    [RelayCommand]
    private void SelectNoRows() => SetAllRowChecks(false);

    [RelayCommand]
    private void ToggleJournal() => IsJournalOpen = !IsJournalOpen;

    [RelayCommand]
    private void ClearJournal() => Journal.Clear();

    partial void OnFindChanged(string value) => SchedulePreview();

    partial void OnReplaceChanged(string value) => SchedulePreview();

    partial void OnModeChanged(BatchRenameMode value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(ModeKey, value.ToString());
        }

        SchedulePreview();
    }

    partial void OnIgnoreCaseChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(IgnoreCaseKey, value ? "true" : "false");
        }

        SchedulePreview();
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

    private void LoadPreferences()
    {
        _suppressPersist = true;
        try
        {
            if (bool.TryParse(_settings.GetStringValue(IgnoreCaseKey), out var ignoreCase))
            {
                IgnoreCase = ignoreCase;
            }

            if (Enum.TryParse<BatchRenameMode>(_settings.GetStringValue(ModeKey), out var mode))
            {
                Mode = mode == BatchRenameMode.Regex ? BatchRenameMode.Regex : BatchRenameMode.Plain;
            }

            if (bool.TryParse(_settings.GetStringValue(StopOnErrorKey), out var stopOnError))
            {
                StopOnError = stopOnError;
            }

            if (bool.TryParse(_settings.GetStringValue(CompactKey), out var compact))
            {
                IsCompact = compact;
            }
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    private void OnSourceOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressEvents || e.PropertyName != nameof(SourceCheckOptionViewModel.IsChecked))
        {
            return;
        }

        SchedulePreview();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressEvents || e.PropertyName != nameof(BatchRenameRowViewModel.IsApplyChecked))
        {
            return;
        }

        RecountActionable();
    }

    private void RebuildSources()
    {
        _suppressEvents = true;
        try
        {
            var previousChecks = Sources.ToDictionary(s => s.SourceId, s => s.IsChecked, StringComparer.Ordinal);

            foreach (var option in Sources)
            {
                option.PropertyChanged -= OnSourceOptionPropertyChanged;
            }

            var referenced = _service.GetReferencedSources(_medias);
            var newSources = referenced
                .Select(info => new SourceCheckOptionViewModel
                {
                    SourceId = info.SourceId,
                    Title = info.Title,
                    IsChecked = !previousChecks.TryGetValue(info.SourceId, out var was) || was,
                })
                .ToList();

            foreach (var option in newSources)
            {
                option.PropertyChanged += OnSourceOptionPropertyChanged;
            }

            Sources.ReplaceAll(newSources);
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

            var newRows = _medias
                .Select(media =>
                {
                    var title = media.Title ?? string.Empty;
                    return new BatchRenameRowViewModel
                    {
                        Media = media,
                        OldTitle = title,
                        NewTitle = title,
                    };
                })
                .ToList();

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
    }

    private void SetAllSources(bool value)
    {
        _suppressEvents = true;
        try
        {
            foreach (var source in Sources)
            {
                source.IsChecked = value;
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        SchedulePreview();
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
}

public sealed record BatchRenameModeOption(BatchRenameMode Value, string Label);
