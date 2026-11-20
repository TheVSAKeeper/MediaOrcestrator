using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class ChronologyViewModel
{
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
            _logger.LogWarning(exception, "Не удалось загрузить плейлисты источника {SourceId}", owner.Id);
            _dialogs.Error("Не удалось загрузить плейлисты", exception.Message);
        }
        finally
        {
            IsLoadingPlaylists = false;
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
            _logger.LogError(exception, "Не удалось сохранить плейлист M3U: {Path}", path);
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
            _logger.LogError(exception, "Не удалось обновить плейлист источника {SourceId}", owner.Id);
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
