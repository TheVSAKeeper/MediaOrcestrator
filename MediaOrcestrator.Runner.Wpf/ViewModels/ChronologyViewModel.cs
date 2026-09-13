using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;

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

    private ChronologyPlan? _plan;

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

    private bool _suppressRecount;

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
        IClipboardService clipboard)
    {
        _orcestrator = orcestrator;
        _publisher = publisher;
        _dialogs = dialogs;
        _settings = settings;
        _filePicker = filePicker;
        _clipboard = clipboard;

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
    private void RefreshSources()
    {
        LoadSources();
    }

    [RelayCommand]
    private async Task LoadPlaylists()
    {
        var owner = OwnerSource?.Source;
        if (owner is null)
        {
            return;
        }

        IsLoadingPlaylists = true;
        try
        {
            var previousId = SelectedPlaylist?.Id;
            var playlists = await Task.Run(() => _publisher.GetPlaylistsAsync(owner));

            ExistingPlaylists.Clear();
            foreach (var playlist in playlists)
            {
                ExistingPlaylists.Add(playlist);
            }

            SelectedPlaylist = ExistingPlaylists.FirstOrDefault(p => p.Id == previousId) ?? ExistingPlaylists.FirstOrDefault();

            if (ExistingPlaylists.Count == 0)
            {
                _dialogs.Info("Плейлисты", "У выбранного аккаунта нет плейлистов – создайте новый.");
            }
        }
        catch (Exception exception)
        {
            _dialogs.Error("Не удалось загрузить плейлисты", exception.Message);
        }
        finally
        {
            IsLoadingPlaylists = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private Task Build()
    {
        return BuildCore(false, CancellationToken.None);
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanBuild))]
    private Task BackfillDates(CancellationToken cancellationToken)
    {
        return BuildCore(true, cancellationToken);
    }

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
        catch (OperationCanceledException)
        {
            StatusText = "Отменено";
        }
        catch (Exception exception)
        {
            _dialogs.Error("Не удалось построить ленту", exception.Message);
            StatusText = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void ExportM3u()
    {
        if (_plan is null)
        {
            return;
        }

        var request = new FileSaveRequest("Сохранить плейлист", "Плейлист M3U (*.m3u)|*.m3u")
        {
            FileName = "chronology.m3u",
            DefaultExtension = "m3u",
        };

        var path = _filePicker.SaveFile(request);

        if (path is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(path, _plan.ToM3u(), new UTF8Encoding(false));
            _dialogs.Info("Готово", $"Сохранено: {path}");
        }
        catch (Exception exception)
        {
            _dialogs.Error("Не удалось сохранить", exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void CopyLinks()
    {
        if (_plan is null)
        {
            return;
        }

        var count = _plan.Items.Count(i => !string.IsNullOrEmpty(i.Url));

        if (_clipboard.TrySetText(_plan.ToUrlList()))
        {
            _dialogs.Info("Скопировано", $"Ссылок в буфере обмена: {count}");
        }
        else
        {
            _dialogs.Error("Не удалось скопировать", "Буфер обмена занят другим приложением – попробуй ещё раз.");
        }
    }

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void SelectAll()
    {
        SetAllSelected(_ => true);
    }

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void SelectNone()
    {
        SetAllSelected(_ => false);
    }

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

    [RelayCommand(CanExecute = nameof(CanCreatePlaylist))]
    private async Task CreatePlaylist()
    {
        var owner = OwnerSource?.Source;
        if (owner is null)
        {
            return;
        }

        var videoIds = Items
            .Where(i => i.IsSelected && !string.IsNullOrEmpty(i.ExternalId))
            .Select(i => i.ExternalId)
            .ToList();

        if (videoIds.Count == 0)
        {
            return;
        }

        const string quotaNote = "Добавление ограничено суточной квотой YouTube API (~200 видео в сутки). При исчерпании дозалейте позже – дубли не добавятся.";

        bool confirmed;
        if (AppendToExisting)
        {
            if (SelectedPlaylist is null)
            {
                return;
            }

            confirmed = _dialogs.Confirm("Дозалить в плейлист",
                $"В плейлист «{SelectedPlaylist.Title}» под аккаунтом «{owner.TitleFull}» будет добавлено до {videoIds.Count} видео (уже присутствующие пропустятся).\n\n{quotaNote}");
        }
        else
        {
            confirmed = _dialogs.Confirm("Создать плейлист на YouTube",
                $"Будет создан плейлист «{PlaylistTitle}» ({SelectedPrivacy.Label}) под аккаунтом «{owner.TitleFull}», в него добавится {videoIds.Count} видео.\n\n{quotaNote}");
        }

        if (!confirmed)
        {
            return;
        }

        try
        {
            var result = AppendToExisting
                ? await _publisher.AppendAsync(owner, SelectedPlaylist!.Id, SelectedPlaylist.Title, videoIds)
                : await _publisher.CreateAsync(owner, PlaylistTitle, SelectedPrivacy.Value, videoIds);

            if (result is null)
            {
                return;
            }

            var lines = new List<string> { $"Добавлено: {result.AddedCount}" };

            if (result.AlreadyPresentCount > 0)
            {
                lines.Add($"Уже были в плейлисте: {result.AlreadyPresentCount}");
            }

            if (result.QuotaExhausted)
            {
                lines.Add("Квота YouTube исчерпана – дозалейте остаток позже, дубли не добавятся.");
            }

            if (!string.IsNullOrEmpty(result.Url))
            {
                lines.Add(result.Url);
            }

            _dialogs.Info(AppendToExisting ? "Дозалив завершён" : "Плейлист создан", string.Join("\n\n", lines));

            if (AppendToExisting)
            {
                LoadPlaylistsCommand.Execute(null);
            }
        }
        catch (Exception exception)
        {
            _dialogs.Error("Не удалось обновить плейлист", exception.Message);
        }
    }

    private bool CanCreatePlaylist()
    {
        if (!HasResults || OwnerSource is null || SelectedCount == 0)
        {
            return false;
        }

        return AppendToExisting
            ? SelectedPlaylist is not null
            : !string.IsNullOrWhiteSpace(PlaylistTitle);
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

public sealed partial class ChronologySourceItem(Source source) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public Source Source => source;

    public string Id => source.Id;

    public string Title => source.TitleFull;
}

public sealed partial class ChronologyRowViewModel(int index, ChronologyItem item, string sourceTitle) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    public int Index => index;

    public string Title => item.Title;

    public string SourceTitle => sourceTitle;

    public string ExternalId => item.ExternalId;

    public string? Url => item.Url;

    public string DateText => item.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "– нет даты –";

    public bool HasDate => item.PublishedAt is not null;
}

public sealed record PrivacyOption(PlaylistPrivacy Value, string Label);
