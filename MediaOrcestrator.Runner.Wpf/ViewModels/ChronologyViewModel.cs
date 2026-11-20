using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class ChronologyViewModel : ObservableObject, IPageHeader
{
    public string PageTitle => "Хронология";

    public string? PageDescription => "Видео нескольких YouTube-источников одним списком по дате публикации.";

    private const string YoutubeTypeId = "Youtube";
    private const string PlaylistExpandedKey = "wpf.chronology.playlist_expanded";

    private readonly Orcestrator _orcestrator;
    private readonly PlaylistPublisher _publisher;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly IFilePicker _filePicker;
    private readonly IClipboardService _clipboard;
    private readonly ILogger<ChronologyViewModel> _logger;

    private ChronologyPlan? _plan;
    private bool _suppressRecount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackfillDatesCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportM3uCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyLinksCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreatePlaylistCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectNoneCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectFirstNCommand))]
    private bool _hasResults;

    [ObservableProperty]
    private int _missingDateCount;

    [ObservableProperty]
    private int _backfillThreads = 4;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreatePlaylistCommand))]
    private int _selectedCount;

    [ObservableProperty]
    private int _firstNCount = 200;

    [ObservableProperty]
    private bool _isPlaylistExpanded;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreatePlaylistCommand))]
    private string _playlistTitle = "Хронология автора";

    [ObservableProperty]
    private PrivacyOption _selectedPrivacy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreatePlaylistCommand))]
    private ChronologySourceItem? _ownerSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionButtonText))]
    [NotifyCanExecuteChangedFor(nameof(CreatePlaylistCommand))]
    private bool _appendToExisting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreatePlaylistCommand))]
    private PlaylistInfo? _selectedPlaylist;

    [ObservableProperty]
    private bool _isLoadingPlaylists;

    public ChronologyViewModel(
        Orcestrator orcestrator,
        PlaylistPublisher publisher,
        IDialogService dialogs,
        ISettingsStore settings,
        IFilePicker filePicker,
        IClipboardService clipboard,
        ILogger<ChronologyViewModel> logger)
    {
        _orcestrator = orcestrator;
        _publisher = publisher;
        _dialogs = dialogs;
        _settings = settings;
        _filePicker = filePicker;
        _clipboard = clipboard;
        _logger = logger;

        _isPlaylistExpanded = settings.GetBool(PlaylistExpandedKey);

        _selectedPrivacy = PrivacyOptions[1];
        LoadSources();
    }

    public ObservableCollection<ChronologySourceItem> Sources { get; } = [];

    public ObservableCollection<ChronologyRowViewModel> Items { get; } = [];

    public ObservableCollection<PlaylistInfo> ExistingPlaylists { get; } = [];

    public string ActionButtonText => AppendToExisting ? "Дозалить в плейлист" : "Создать плейлист";

    public IReadOnlyList<PrivacyOption> PrivacyOptions { get; } =
    [
        new(PlaylistPrivacy.Private, "Приватный"),
        new(PlaylistPrivacy.Unlisted, "По ссылке"),
        new(PlaylistPrivacy.Public, "Открытый"),
    ];

    public bool HasMissingDates => MissingDateCount > 0;

    partial void OnMissingDateCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMissingDates));
    }

    partial void OnIsPlaylistExpandedChanged(bool value)
    {
        _settings.SetBool(PlaylistExpandedKey, value);
    }

    partial void OnAppendToExistingChanged(bool value)
    {
        if (value && OwnerSource is not null && ExistingPlaylists.Count == 0)
        {
            LoadPlaylistsCommand.Execute(null);
        }
    }

    partial void OnOwnerSourceChanged(ChronologySourceItem? value)
    {
        if (AppendToExisting && value is not null)
        {
            LoadPlaylistsCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void RefreshSources() => LoadSources();

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private Task Build() => BuildCore(false, CancellationToken.None);

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanBuild))]
    private Task BackfillDates(CancellationToken cancellationToken) => BuildCore(true, cancellationToken);

    private bool CanBuild() => !IsLoading;

    private async Task BuildCore(bool backfill, CancellationToken cancellationToken)
    {
        var ids = Sources.Where(s => s.IsSelected).Select(s => s.Id).ToList();
        if (ids.Count == 0)
        {
            _dialogs.Info("Не выбраны источники", "Отметьте хотя бы один YouTube-источник, чтобы построить ленту.");
            return;
        }

        IsLoading = true;
        StatusText = backfill ? "Дополняю недостающие даты…" : "Строю ленту…";

        try
        {
            var progress = new Progress<string>(text => StatusText = text);
            var plan = await Task.Run(() => _orcestrator.BuildChronologyAsync(ids, backfill, BackfillThreads, progress, cancellationToken), cancellationToken);

            var titles = Sources.ToDictionary(s => s.Id, s => s.Title, StringComparer.OrdinalIgnoreCase);

            _plan = plan;
            Items.Clear();
            var index = 1;
            foreach (var item in plan.Items)
            {
                var row = new ChronologyRowViewModel(index++, item, titles.GetValueOrDefault(item.SourceId, item.SourceId));
                row.PropertyChanged += OnRowPropertyChanged;
                Items.Add(row);
            }

            MissingDateCount = plan.MissingDateCount;
            HasResults = Items.Count > 0;
            RecountSelected();
            StatusText = $"Видео в ленте: {Items.Count}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Отменено";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось построить хронологию для источников {SourceIds}", ids);
            _dialogs.Error("Не удалось построить ленту", exception.Message);
            StatusText = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void SelectAll() => SetAllSelected(_ => true);

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void SelectNone() => SetAllSelected(_ => false);

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void SelectFirstN()
    {
        var take = Math.Max(0, FirstNCount);
        SetAllSelected(index => index < take);
    }

    [RelayCommand]
    private void SelectOnly(ChronologyRowViewModel? row)
    {
        if (row is not null)
        {
            SetAllSelected(i => ReferenceEquals(Items[i], row));
        }
    }

    [RelayCommand]
    private void SelectUpTo(ChronologyRowViewModel? row)
    {
        var index = row is null ? -1 : Items.IndexOf(row);
        if (index >= 0)
        {
            SetAllSelected(i => i <= index);
        }
    }

    [RelayCommand]
    private void SelectFromHere(ChronologyRowViewModel? row)
    {
        var index = row is null ? -1 : Items.IndexOf(row);
        if (index >= 0)
        {
            SetAllSelected(i => i >= index);
        }
    }

    private void SetAllSelected(Func<int, bool> predicate)
    {
        _suppressRecount = true;
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].IsSelected = predicate(i);
        }

        _suppressRecount = false;
        RecountSelected();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_suppressRecount && e.PropertyName == nameof(ChronologyRowViewModel.IsSelected))
        {
            RecountSelected();
        }
    }

    private void RecountSelected()
    {
        SelectedCount = Items.Count(i => i.IsSelected);
    }

    private void LoadSources()
    {
        var previouslySelected = Sources.Where(s => s.IsSelected).Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousOwnerId = OwnerSource?.Id;

        Sources.Clear();

        foreach (var source in _orcestrator.GetSources()
                     .Where(s => string.Equals(s.TypeId, YoutubeTypeId, StringComparison.OrdinalIgnoreCase) && !s.IsDisable))
        {
            Sources.Add(new(source) { IsSelected = previouslySelected.Contains(source.Id) });
        }

        OwnerSource = Sources.FirstOrDefault(s => s.Id == previousOwnerId) ?? Sources.FirstOrDefault();
    }
}
