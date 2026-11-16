using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

internal static class PublishSuggestionHelper
{
    private const int MaxTitleSuggestions = 20;
    private const int MaxDescriptionTemplates = 10;

    public static string? TryReadSidecarDescription(string videoPath, ILogger logger)
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
            logger.LogWarning(exception, "Не удалось прочитать описание рядом с видео {Path}", videoPath);
        }

        return null;
    }

    public static SourceSuggestionsResult BuildForSource(
        IEnumerable<Media> medias,
        string sourceId,
        string? excludedSidecarDescription)
    {
        var links = medias
            .SelectMany(x => x.Sources ?? [])
            .Where(x => x.SourceId == sourceId)
            .OrderByDescending(x => x.SortNumber)
            .ToList();

        var titles = links.Select(x => x.Title)
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxTitleSuggestions)
            .ToList();

        var titleToDescription = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var link in links)
        {
            if (string.IsNullOrEmpty(link.Title) || string.IsNullOrWhiteSpace(link.Description))
            {
                continue;
            }

            titleToDescription.TryAdd(link.Title, link.Description);
        }

        var descriptions = links.Select(x => x.Description)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !string.Equals(x, excludedSidecarDescription, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxDescriptionTemplates)
            .Select(d => new DescriptionTemplateViewModel(d, isFromVideoFile: false))
            .ToList();

        return new(titles, titleToDescription, descriptions);
    }

    public static string? TryFormatFileSize(string path)
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

    public static string FormatFileSize(long bytes)
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

internal sealed record SourceSuggestionsResult(
    IReadOnlyList<string> Titles,
    IReadOnlyDictionary<string, string> TitleToDescription,
    IReadOnlyList<DescriptionTemplateViewModel> Descriptions);

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
