using System.Globalization;
using System.Text;

namespace MediaOrcestrator.Domain.Merging;

public static class MergeCandidateFinder
{
    public static IReadOnlyList<MergeCandidateGroup> FindGroups(
        IReadOnlyList<Media> allMedia,
        IReadOnlySet<string>? sourceScope = null)
    {
        var buckets = new Dictionary<string, List<Media>>();

        foreach (var media in allMedia)
        {
            if (string.IsNullOrWhiteSpace(media.Title))
            {
                continue;
            }

            if (sourceScope != null && !MatchesScope(media, sourceScope))
            {
                continue;
            }

            var key = Normalize(media.Title);

            if (key.Length == 0)
            {
                continue;
            }

            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = [];
                buckets[key] = bucket;
            }

            bucket.Add(media);
        }

        var groups = new List<MergeCandidateGroup>();

        foreach (var (key, bucket) in buckets)
        {
            if (bucket.Count < 2)
            {
                continue;
            }

            if (HasSourceCollision(bucket))
            {
                continue;
            }

            groups.Add(new()
            {
                NormalizedKey = key,
                Medias = bucket,
                SuggestedTarget = MediaMergeService.ChooseTarget(bucket),
            });
        }

        return groups
            .OrderByDescending(g => g.TotalMediaCount)
            .ThenBy(g => g.NormalizedKey, StringComparer.Ordinal)
            .ToList();
    }

    // TODO: #66 recall падает на префиксах-тегах: "🔥 Обзор" / "[HD] Обзор" / "Обзор" дают три разных ключа — для пользовательских заголовков с emoji/скобочными тегами группировка пропустит явные дубли. Рассмотреть отдельную стадию очистки префиксов или fuzzy-матчинг.
    public static string Normalize(string title)
    {
        var decomposed = title.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;

        foreach (var rune in decomposed.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);

            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (Rune.IsLetterOrDigit(rune))
            {
                foreach (var lowered in Rune.ToLowerInvariant(rune).ToString())
                {
                    builder.Append(lowered);
                }

                previousWasSpace = false;
                continue;
            }

            if (previousWasSpace || builder.Length == 0)
            {
                continue;
            }

            builder.Append(' ');
            previousWasSpace = true;
        }

        if (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length -= 1;
        }

        return builder.ToString();
    }

    private static bool MatchesScope(Media media, IReadOnlySet<string> sourceScope)
    {
        return media.Sources?.Any(link => sourceScope.Contains(link.SourceId)) == true;
    }

    private static bool HasSourceCollision(IReadOnlyList<Media> medias)
    {
        var seen = new HashSet<string>();

        return medias
            .SelectMany(m => m.Sources ?? [])
            .Any(link => !seen.Add(link.SourceId));
    }
}
