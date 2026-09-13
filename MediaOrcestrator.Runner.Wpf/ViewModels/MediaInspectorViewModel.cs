using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Converters;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MediaInspectorViewModel(
    ILogger<MediaInspectorViewModel> logger,
    ISettingsStore settings,
    IShellLauncher shellLauncher,
    IClipboardService clipboard) : ObservableObject
{
    private const string SourcesCompactSettingKey = "wpf.media.inspector.sources.compact";

    private readonly ILogger<MediaInspectorViewModel> _logger = logger;
    private readonly ISettingsStore _settings = settings;
    private readonly IShellLauncher _shellLauncher = shellLauncher;
    private readonly IClipboardService _clipboard = clipboard;

    private Media? _media;
    private IReadOnlyList<Source> _allSources = [];
    private int _previewLoadToken;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string _description = string.Empty;

    [ObservableProperty]
    private string? _previewUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewImage))]
    [NotifyPropertyChangedFor(nameof(ShowNoPreview))]
    private ImageSource? _previewImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoPreview))]
    private bool _isPreviewLoading;

    [ObservableProperty]
    private bool _isMultiSelection;

    [ObservableProperty]
    private int _selectionCount;

    [ObservableProperty]
    private bool _areSourcesCompact = bool.TryParse(settings.GetStringValue(SourcesCompactSettingKey), out var compact) && compact;

    public RangeObservableCollection<MediaInspectorSourceItem> Sources { get; } = [];

    public bool HasSources => Sources.Count > 0;

    public RangeObservableCollection<MediaMetadataGroup> UserMetadataGroups { get; } = [];

    public RangeObservableCollection<MediaMetadataItem> SystemMetadata { get; } = [];

    public RangeObservableCollection<MediaPreviewSource> PreviewSources { get; } = [];

    public bool HasMultiplePreviews => PreviewSources.Count > 1;

    public int UserMetadataCount => UserMetadataGroups.Sum(g => g.Items.Count);

    public int SystemMetadataCount => SystemMetadata.Count;

    public bool HasUserMetadata => UserMetadataCount > 0;

    public bool HasSystemMetadata => SystemMetadata.Count > 0;

    public bool HasPreviewImage => PreviewImage is not null;

    public bool ShowNoPreview => !IsPreviewLoading && PreviewImage is null;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool CanCopyDetails => !IsMultiSelection && _media is not null;

    public void ShowSingle(Media media, IReadOnlyList<Source> allSources)
    {
        IsMultiSelection = false;
        SelectionCount = 1;
        _media = media;
        _allSources = allSources;
        Title = media.Title;
        Description = media.Description ?? string.Empty;

        var sourceById = allSources.ToDictionary(s => s.Id);

        Sources.ReplaceAll((media.Sources ?? []).Select(link =>
        {
            sourceById.TryGetValue(link.SourceId ?? string.Empty, out var source);
            var status = MediaStatusHelper.GetById(link.Status);

            return new MediaInspectorSourceItem
            {
                SourceTitle = source?.TitleFull ?? link.SourceId ?? "?",
                LinkTitle = string.IsNullOrEmpty(link.Title) ? null : link.Title,
                LinkDescription = string.IsNullOrEmpty(link.Description) ? null : link.Description,
                ExternalId = link.ExternalId ?? string.Empty,
                StatusIcon = StatusIcons.Resolve(status.Id),
                StatusText = status.Text,
                StatusBrush = MakeBrush(status.IconColor),
                OpenUri = TryGetExternalUri(source, link, media),
            };
        }));

        var userItems = new List<(MetadataItem source, MediaMetadataItem display)>();
        var systemItems = new List<MediaMetadataItem>();

        foreach (var item in media.Metadata ?? [])
        {
            var entry = ToInspectorItem(item, sourceById);

            if (IsSystemKey(item.Key))
            {
                systemItems.Add(entry);
            }
            else
            {
                userItems.Add((item, entry));
            }
        }

        BuildUserGroups(userItems);

        SystemMetadata.ReplaceAll(systemItems.OrderBy(i => i.DisplayName, NaturalStringComparer.Instance));

        BuildPreviewSources(media, sourceById);

        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(HasUserMetadata));
        OnPropertyChanged(nameof(HasSystemMetadata));
        OnPropertyChanged(nameof(UserMetadataCount));
        OnPropertyChanged(nameof(SystemMetadataCount));
        OnPropertyChanged(nameof(CanCopyDetails));
        CopyDetailsCommand.NotifyCanExecuteChanged();
    }

    public void Clear()
    {
        IsMultiSelection = false;
        SelectionCount = 0;
        Title = string.Empty;
        Description = string.Empty;
        Sources.ReplaceAll([]);
        UserMetadataGroups.ReplaceAll([]);
        SystemMetadata.ReplaceAll([]);
        PreviewSources.ReplaceAll([]);
        ApplyPreviewSelection(null);
        _media = null;
        _allSources = [];

        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(HasUserMetadata));
        OnPropertyChanged(nameof(HasSystemMetadata));
        OnPropertyChanged(nameof(UserMetadataCount));
        OnPropertyChanged(nameof(SystemMetadataCount));
        OnPropertyChanged(nameof(HasMultiplePreviews));
        OnPropertyChanged(nameof(CanCopyDetails));
        CopyDetailsCommand.NotifyCanExecuteChanged();
    }

    public void ShowMulti(int count)
    {
        IsMultiSelection = true;
        SelectionCount = count;
        Title = $"Выделено {count}";
        Description = string.Empty;
        Sources.ReplaceAll([]);
        UserMetadataGroups.ReplaceAll([]);
        SystemMetadata.ReplaceAll([]);
        PreviewSources.ReplaceAll([]);
        ApplyPreviewSelection(null);
        _media = null;
        _allSources = [];

        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(HasUserMetadata));
        OnPropertyChanged(nameof(HasSystemMetadata));
        OnPropertyChanged(nameof(UserMetadataCount));
        OnPropertyChanged(nameof(SystemMetadataCount));
        OnPropertyChanged(nameof(HasMultiplePreviews));
        OnPropertyChanged(nameof(CanCopyDetails));
        CopyDetailsCommand.NotifyCanExecuteChanged();
    }

    partial void OnAreSourcesCompactChanged(bool value)
    {
        _settings.SetValue(SourcesCompactSettingKey, value ? "true" : "false");
    }

    [RelayCommand]
    private void SetPreview(MediaPreviewSource? source)
    {
        if (source is null || string.Equals(source.Url, PreviewUrl, StringComparison.Ordinal))
        {
            return;
        }

        _logger.LogDebug("Превью переключено вручную на {Preview} ({Source}) для медиа {MediaId}",
            source.Url, source.SourceTitle, _media?.Id);

        ApplyPreviewSelection(source.Url);
    }

    private void BuildPreviewSources(Media media, IDictionary<string, Source> sourceById)
    {
        var sources = media.Metadata
            .Where(m => string.Equals(m.Key, "PreviewUrl", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(m.Value))
            .Select(m =>
            {
                var url = m.Value.Trim();

                return new MediaPreviewSource
                {
                    SourceTitle = ResolveSourceTitle(m.SourceId, sourceById),
                    Url = url,
                    CanRender = StringToImageSourceConverter.CanRender(url),
                };
            })
            .ToList();

        PreviewSources.ReplaceAll(sources);
        OnPropertyChanged(nameof(HasMultiplePreviews));

        if (sources.Count == 0)
        {
            _logger.LogDebug("Превью нет: у медиа {MediaId} не задан ни один PreviewUrl", media.Id);
            ApplyPreviewSelection(null);

            return;
        }

        var auto = sources.FirstOrDefault(s => s.CanRender);

        if (auto is null)
        {
            _logger.LogWarning(
                "Превью не отображается: ни один из {Count} PreviewUrl медиа {MediaId} не пригоден (относительные/неподдерживаемая схема/нет файла): {Candidates}",
                sources.Count, media.Id, string.Join("; ", sources.Select(s => s.Url)));
            ApplyPreviewSelection(null);

            return;
        }

        if (sources.Count > 1)
        {
            _logger.LogDebug("Для медиа {MediaId} выбрано превью {Preview} ({Source}) из {Count} кандидатов",
                media.Id, auto.Url, auto.SourceTitle, sources.Count);
        }

        ApplyPreviewSelection(auto.Url);
    }

    private void ApplyPreviewSelection(string? url)
    {
        PreviewUrl = url;

        foreach (var source in PreviewSources)
        {
            source.IsActive = string.Equals(source.Url, url, StringComparison.Ordinal);
        }

        var token = ++_previewLoadToken;
        PreviewImage = null;

        if (url is null || !StringToImageSourceConverter.CanRender(url))
        {
            IsPreviewLoading = false;

            return;
        }

        IsPreviewLoading = true;
        _ = LoadPreviewAsync(StringToImageSourceConverter.NormalizeUrl(url)!, token);
    }

    private async Task LoadPreviewAsync(string url, int token)
    {
        var image = await MediaPreviewCache.GetAsync(url);

        if (token != _previewLoadToken)
        {
            return;
        }

        PreviewImage = image;
        IsPreviewLoading = false;
    }

    private static string ResolveSourceTitle(string? sourceId, IDictionary<string, Source> sourceById)
    {
        if (!string.IsNullOrEmpty(sourceId) && sourceById.TryGetValue(sourceId, out var source))
        {
            return source.TitleFull;
        }

        return string.IsNullOrEmpty(sourceId) ? "Общее" : sourceId;
    }

    [RelayCommand]
    private void CopyItem(MediaMetadataItem? item)
    {
        if (item is null)
        {
            return;
        }

        TryCopy(item.RawValue);
    }

    [RelayCommand]
    private void CopyItemFormatted(MediaMetadataItem? item)
    {
        if (item is null)
        {
            return;
        }

        TryCopy($"{item.DisplayName}: {item.Value}");
    }

    [RelayCommand]
    private void CopyTitle()
    {
        TryCopy(Title);
    }

    [RelayCommand]
    private void CopyDescription()
    {
        if (!HasDescription)
        {
            return;
        }

        TryCopy(Description);
    }

    [RelayCommand]
    private void CopyExternalId(string? externalId)
    {
        if (string.IsNullOrEmpty(externalId))
        {
            return;
        }

        TryCopy(externalId);
    }

    [RelayCommand]
    private void OpenSource(MediaInspectorSourceItem? item)
    {
        if (item?.OpenUri is null)
        {
            return;
        }

        _shellLauncher.Open(item.OpenUri.ToString());
    }

    [RelayCommand(CanExecute = nameof(CanCopyDetails))]
    private void CopyDetails()
    {
        if (_media is null)
        {
            return;
        }

        var details = BuildDetails(_media, _allSources);
        TryCopy(details);
    }

    private void BuildUserGroups(IReadOnlyList<(MetadataItem source, MediaMetadataItem display)> items)
    {
        var comparer = NaturalStringComparer.Instance;

        var grouped = items
            .GroupBy(x => x.display.DisplayName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, comparer);

        var newGroups = new List<MediaMetadataGroup>();

        foreach (var group in grouped)
        {
            var groupVm = new MediaMetadataGroup(group.Key);
            groupVm.Items.AddRange(group
                .Select(x => x.display)
                .OrderBy(x => x.SourceTitle ?? string.Empty, comparer));
            newGroups.Add(groupVm);
        }

        UserMetadataGroups.ReplaceAll(newGroups);
    }

    private static bool IsSystemKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        return key.StartsWith("_system", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "PreviewUrl", StringComparison.OrdinalIgnoreCase);
    }

    private static MediaMetadataItem ToInspectorItem(MetadataItem item, IDictionary<string, Source> sourceById)
    {
        string? sourceTitle = null;

        if (!string.IsNullOrEmpty(item.SourceId)
            && sourceById.TryGetValue(item.SourceId, out var source))
        {
            sourceTitle = source.TitleFull;
        }
        else if (!string.IsNullOrEmpty(item.SourceId))
        {
            sourceTitle = item.SourceId;
        }

        return new(item.DisplayName ?? item.Key,
            MetadataValueFormatter.Format(item),
            item.Value,
            sourceTitle);
    }

    private static Uri? TryGetExternalUri(Source? source, MediaSourceLink link, Media media)
    {
        if (source?.Type is null || string.IsNullOrEmpty(link.ExternalId))
        {
            return null;
        }

        try
        {
            var linkMetadata = media.Metadata?.ForSource(source.Id);

            return source.Type.GetExternalUri(link.ExternalId, source.Settings, linkMetadata);
        }
        catch
        {
            return null;
        }
    }

    private static SolidColorBrush MakeBrush(System.Drawing.Color color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();

        return brush;
    }

    private void TryCopy(string value)
    {
        _clipboard.TrySetText(value ?? string.Empty);
    }

    private static string BuildDetails(Media media, IReadOnlyList<Source> allSources)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Название: {media.Title}");

        if (!string.IsNullOrEmpty(media.Description))
        {
            sb.AppendLine($"Описание: {media.Description}");
        }

        if (media.Metadata is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Метаданные:");

            foreach (var meta in media.Metadata)
            {
                if (IsSystemKey(meta.Key))
                {
                    continue;
                }

                var name = meta.DisplayName ?? meta.Key;
                sb.AppendLine($"  {name}: {MetadataValueFormatter.Format(meta)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Источники:");

        if (media.Sources is not { Count: > 0 })
        {
            sb.AppendLine("  Нет источников");

            return sb.ToString();
        }

        foreach (var link in media.Sources)
        {
            var source = allSources.FirstOrDefault(s => s.Id == link.SourceId);
            var sourceName = source?.TitleFull ?? "Неизвестный источник";
            var status = MediaStatusHelper.GetById(link.Status);

            sb.AppendLine($"  {sourceName}: {status.IconText} {status.Text}");

            if (!string.IsNullOrEmpty(link.ExternalId))
            {
                sb.AppendLine($"    ID: {link.ExternalId}");
            }
        }

        return sb.ToString();
    }
}

public sealed class MediaInspectorSourceItem
{
    public required string SourceTitle { get; init; }

    public string? LinkTitle { get; init; }

    public string? LinkDescription { get; init; }

    public required string ExternalId { get; init; }

    public required PackIconLucideKind StatusIcon { get; init; }

    public required string StatusText { get; init; }

    public required Brush StatusBrush { get; init; }

    public Uri? OpenUri { get; init; }

    public bool CanOpen => OpenUri is not null;
}

public sealed class MediaMetadataGroup(string label)
{
    public string Label { get; } = label;

    public RangeObservableCollection<MediaMetadataItem> Items { get; } = [];
}

public sealed record MediaMetadataItem(string DisplayName, string Value, string RawValue, string? SourceTitle);

public sealed partial class MediaPreviewSource : ObservableObject
{
    public required string SourceTitle { get; init; }

    public required string Url { get; init; }

    public required bool CanRender { get; init; }

    [ObservableProperty]
    private bool _isActive;

    public string Label => CanRender ? SourceTitle : $"{SourceTitle} – недоступно";
}
