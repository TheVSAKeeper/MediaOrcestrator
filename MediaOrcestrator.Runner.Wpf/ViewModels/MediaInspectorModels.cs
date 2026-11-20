using CommunityToolkit.Mvvm.ComponentModel;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

internal static class MediaInspectorDetailsFormatter
{
    public static bool IsSystemKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        return key.StartsWith("_system", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "PreviewUrl", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveSourceTitle(string? sourceId, IDictionary<string, Source> sourceById)
    {
        if (!string.IsNullOrEmpty(sourceId) && sourceById.TryGetValue(sourceId, out var source))
        {
            return source.TitleFull;
        }

        return string.IsNullOrEmpty(sourceId) ? "Общее" : sourceId;
    }

    public static MediaMetadataItem ToInspectorItem(MetadataItem item, IDictionary<string, Source> sourceById)
    {
        string? sourceTitle = null;

        if (!string.IsNullOrEmpty(item.SourceId) && sourceById.TryGetValue(item.SourceId, out var source))
        {
            sourceTitle = source.TitleFull;
        }
        else if (!string.IsNullOrEmpty(item.SourceId))
        {
            sourceTitle = item.SourceId;
        }

        return new(
            item.DisplayName ?? item.Key,
            MetadataValueFormatter.Format(item),
            item.Value,
            sourceTitle);
    }

    public static Uri? TryGetExternalUri(Source? source, MediaSourceLink link, Media media, ILogger logger)
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось получить ссылку на медиа {MediaId} из источника {SourceId}", media.Id, source.Id);
            return null;
        }
    }

    public static string BuildDetails(Media media, IReadOnlyList<Source> allSources)
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
