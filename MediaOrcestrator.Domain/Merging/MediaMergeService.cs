using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Domain.Merging;

public sealed class MediaMergeService(Orcestrator orcestrator, ILogger<MediaMergeService> logger)
{
    public static Media ChooseTarget(IReadOnlyList<Media> medias)
    {
        return medias
            .OrderByDescending(m => m.Sources?.Count ?? 0)
            .ThenByDescending(m => !string.IsNullOrWhiteSpace(m.Description))
            .First();
    }

    public MergePreview BuildPreview(IReadOnlyList<Media> selectedMedia, Media? explicitTarget = null)
    {
        if (selectedMedia.Count < 2)
        {
            throw new InvalidOperationException("Для объединения необходимо выбрать как минимум 2 медиа");
        }

        var targetMedia = explicitTarget ?? ChooseTarget(selectedMedia);

        if (!selectedMedia.Any(m => ReferenceEquals(m, targetMedia)))
        {
            throw new InvalidOperationException("Указанное целевое медиа отсутствует в выборке");
        }

        var sourceMedias = selectedMedia.Where(m => !ReferenceEquals(m, targetMedia)).ToList();
        var conflicts = new List<string>();
        var sourceDict = new Dictionary<string, MediaSourceLink>();

        foreach (var sourceLink in targetMedia.Sources ?? [])
        {
            sourceDict[sourceLink.SourceId] = sourceLink;
        }

        var allSources = orcestrator.GetSources();

        foreach (var media in sourceMedias)
        {
            foreach (var sourceLink in media.Sources ?? [])
            {
                if (sourceDict.TryAdd(sourceLink.SourceId, sourceLink))
                {
                    continue;
                }

                var source = allSources.FirstOrDefault(s => s.Id == sourceLink.SourceId);
                var sourceName = source?.TitleFull ?? sourceLink.SourceId;
                conflicts.Add($"Источник '{sourceName}' присутствует в нескольких медиа");
            }
        }

        return new()
        {
            TargetMedia = targetMedia,
            SourceMedias = sourceMedias,
            ResultingSources = sourceDict.Values.ToList(),
            Conflicts = conflicts,
        };
    }

    public void Apply(MergePreview preview, bool acceptConflicts = false)
    {
        if (preview.HasConflicts && !acceptConflicts)
        {
            throw new MergeConflictException(preview.Conflicts);
        }

        logger.LogInformation("Объединение медиа. Целевое: '{TargetTitle}' ({TargetId}), присоединяется: {Count}",
            preview.TargetMedia.Title,
            preview.TargetMedia.Id,
            preview.SourceMedias.Count);

        var originalSources = preview.TargetMedia.Sources;
        preview.TargetMedia.Sources = preview.ResultingSources.ToList();

        try
        {
            orcestrator.MergeMedia(preview.TargetMedia, preview.SourceMedias);
        }
        catch
        {
            preview.TargetMedia.Sources = originalSources;
            throw;
        }

        logger.LogInformation("Объединение завершено. Итого источников: {TotalSourcesCount}",
            preview.TotalSourcesCount);
    }
}
