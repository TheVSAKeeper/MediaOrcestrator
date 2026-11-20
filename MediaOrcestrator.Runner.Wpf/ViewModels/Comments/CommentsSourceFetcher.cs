using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

internal sealed class CommentsSourceFetcher(
    Orcestrator orcestrator,
    CommentsService commentsService,
    ActionHolder actionHolder,
    IDialogService dialogs,
    ILogger logger,
    IShellLauncher shellLauncher,
    IUiDispatcher dispatcher)
{
    public async Task FetchFromSourcesAsync(
        IReadOnlyList<Source> selectedSources,
        CommentsViewSettings settings,
        CancellationTokenSource fetchCts,
        Action<CommentsFetchProgress> onProgress)
    {
        var supported = selectedSources.Where(s => s.Type is ISupportsComments).ToList();
        var unsupported = selectedSources.Where(s => s.Type is not ISupportsComments).Select(s => s.TitleFull).ToList();

        if (unsupported.Count > 0)
        {
            dialogs.Info("Невозможно загрузить", $"Источники не поддерживают комментарии: {string.Join(", ", unsupported)}.");
        }

        if (supported.Count == 0)
        {
            return;
        }

        var sinceDays = settings.FetchSinceDays > 0 ? (int?)settings.FetchSinceDays : null;
        var since = sinceDays is not null ? DateTime.UtcNow.AddDays(-sinceDays.Value) : (DateTime?)null;
        var takeRecent = settings.FetchOnlyRecent > 0 ? (int?)settings.FetchOnlyRecent : null;

        var plans = supported
            .Select(src => (Source: src, Targets: BuildTargetsForSource(src, since, takeRecent)))
            .Where(p => p.Targets.Count > 0)
            .ToList();

        var total = plans.Sum(p => p.Targets.Count);

        if (total == 0)
        {
            dialogs.Info("Нечего загружать", "В выбранных источниках нет медиа для загрузки.");
            return;
        }

        var token = fetchCts.Token;
        var sourcesLabel = plans.Count == 1
            ? $"«{plans[0].Source.TitleFull}»"
            : $"{plans.Count} ист.";

        onProgress(new(0, total, $"Загрузка комментариев из {sourcesLabel}…", $"0 / {total}"));

        var rootAction = actionHolder.Register(
            $"Загрузка комментариев: {sourcesLabel}",
            "Запущена",
            total,
            fetchCts,
            kind: ActionKind.Comments);

        var counters = new FetchCounters();
        var fetchSw = Stopwatch.StartNew();

        logger.LogInformation(
            "Старт загрузки комментариев: источников {Sources}, медиа {Total}, since={Since}, takeRecent={TakeRecent}",
            plans.Count,
            total,
            since,
            takeRecent);

        try
        {
            await Task.WhenAll(plans.Select(plan => RunSourceFetchAsync(plan, settings, rootAction, counters, total, onProgress, token)));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            fetchSw.Stop();

            var ok = Volatile.Read(ref counters.Ok);
            var failed = Volatile.Read(ref counters.Failed);

            var summary = failed > 0
                ? $"Успешно {ok} из {total}, ошибок {failed}"
                : $"Успешно {ok} из {total}";

            if (token.IsCancellationRequested)
            {
                rootAction.MarkCancelled(summary);
                logger.LogInformation(
                    "Загрузка комментариев отменена за {Elapsed} мс: ok {Ok}, failed {Failed}, всего {Total}",
                    fetchSw.ElapsedMilliseconds,
                    ok,
                    failed,
                    total);
            }
            else if (failed > 0 && ok == 0)
            {
                rootAction.Fail(summary);
                logger.LogWarning(
                    "Загрузка комментариев провалилась за {Elapsed} мс: ok 0, failed {Failed} из {Total}",
                    fetchSw.ElapsedMilliseconds,
                    failed,
                    total);
            }
            else
            {
                rootAction.Finish(summary);
                logger.LogInformation(
                    "Загрузка комментариев завершена за {Elapsed} мс: ok {Ok}, failed {Failed} из {Total}",
                    fetchSw.ElapsedMilliseconds,
                    ok,
                    failed,
                    total);
            }
        }
    }

    public async Task RefreshSourceMediaAsync(string sourceId, string externalMediaId)
    {
        var sources = orcestrator.GetSources().ToDictionary(s => s.Id);

        if (!sources.TryGetValue(sourceId, out var source) || source.Type is not ISupportsComments)
        {
            return;
        }

        var media = orcestrator.GetMedias()
            .FirstOrDefault(m => m.Sources.Any(l => l.SourceId == sourceId && l.ExternalId == externalMediaId));
        var link = media?.Sources.FirstOrDefault(l => l.SourceId == sourceId && l.ExternalId == externalMediaId);

        if (media is null || link is null)
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        var action = actionHolder.Register(
            $"Комментарии: «{media.Title}»",
            "Загрузка…",
            1,
            cts,
            kind: ActionKind.Comments);

        try
        {
            var progress = new Progress<string>(text => action.Status = text);
            var count = await commentsService.RefreshAsync(source, media, link, progress, cts.Token);
            action.Status = $"{source.TitleFull}: {count}";
            action.ProgressPlus();
            action.Finish();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            action.MarkCancelled("Отменено");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось обновить комментарии для «{Title}» в {Source}", media.Title, source.TitleFull);
            action.Fail("Ошибка: " + ex.Message, ex);
        }
    }

    public Task OpenOneSourceAsync(MediaGroupSourceActionViewModel action)
    {
        var source = orcestrator.GetSources().FirstOrDefault(s => s.Id == action.SourceId);

        if (source?.Type is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            var uri = source.Type.GetExternalUri(action.ExternalMediaId, source.Settings);

            if (uri is null)
            {
                return Task.CompletedTask;
            }

            if (!shellLauncher.Open(uri.ToString()))
            {
                logger.LogError("Не удалось открыть медиа в источнике {Source}", source.TitleFull);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось открыть медиа в источнике {Source}", source.TitleFull);
        }

        return Task.CompletedTask;
    }

    private async Task RunSourceFetchAsync(
        (Source Source, List<(Media Media, MediaSourceLink Link)> Targets) plan,
        CommentsViewSettings settings,
        ActionHolder.RunningAction rootAction,
        FetchCounters counters,
        int total,
        Action<CommentsFetchProgress> onProgress,
        CancellationToken rootToken)
    {
        var source = plan.Source;
        var targets = plan.Targets;

        using var childCts = CancellationTokenSource.CreateLinkedTokenSource(rootToken);
        var sourceAction = actionHolder.Register(
            $"Источник: «{source.TitleFull}»",
            $"0 / {targets.Count}",
            targets.Count,
            childCts,
            kind: ActionKind.Comments,
            parent: rootAction);

        var srcOk = 0;
        var srcFailed = 0;

        try
        {
            var maxParallelism = settings.FetchMaxParallelism <= 0
                ? CommentsViewSettings.DefaultFetchMaxParallelism
                : Math.Clamp(settings.FetchMaxParallelism, CommentsViewSettings.MinFetchMaxParallelism, CommentsViewSettings.MaxFetchMaxParallelism);

            await Parallel.ForEachAsync(targets, new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = childCts.Token,
            }, async (item, ct) =>
            {
                var (media, link) = item;

                try
                {
                    await commentsService.RefreshAsync(source, media, link, null, ct);
                    Interlocked.Increment(ref counters.Ok);
                    Interlocked.Increment(ref srcOk);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref counters.Failed);
                    Interlocked.Increment(ref srcFailed);
                    logger.LogError(ex, "Не удалось загрузить комментарии для «{Title}»", media.Title);
                }

                rootAction.ProgressPlus();
                sourceAction.ProgressPlus();

                var processed = Interlocked.Increment(ref counters.Processed);
                var ok = Volatile.Read(ref counters.Ok);
                var failed = Volatile.Read(ref counters.Failed);

                rootAction.Status = $"{source.TitleFull}: {media.Title}";
                sourceAction.Status = media.Title;

                var message = $"[{processed}/{total}] {source.TitleFull}: «{media.Title}»";
                var counter = failed > 0 ? $"✓ {ok}  ✗ {failed}  /  {total}" : $"✓ {ok}  /  {total}";
                dispatcher.Invoke(() => onProgress(new(processed, total, message, counter)));
            });
        }
        catch (OperationCanceledException) when (childCts.IsCancellationRequested)
        {
        }
        finally
        {
            var okFinal = Volatile.Read(ref srcOk);
            var failedFinal = Volatile.Read(ref srcFailed);

            var srcSummary = $"«{source.TitleFull}»: успешно {okFinal} из {targets.Count}";

            if (failedFinal > 0)
            {
                srcSummary += $", ошибок {failedFinal}";
            }

            if (childCts.IsCancellationRequested)
            {
                sourceAction.MarkCancelled(srcSummary);
            }
            else if (failedFinal > 0)
            {
                sourceAction.Fail(srcSummary);
            }
            else
            {
                sourceAction.Finish(srcSummary);
            }
        }
    }

    private List<(Media Media, MediaSourceLink Link)> BuildTargetsForSource(Source source, DateTime? since, int? takeRecent)
    {
        var all = orcestrator.GetMedias()
            .SelectMany(m => m.Sources
                .Where(l => l.SourceId == source.Id && !string.IsNullOrEmpty(l.ExternalId))
                .Select(l => (Media: m, Link: l)))
            .OrderByDescending(item => item.Link.SortNumber)
            .ToList();

        IEnumerable<(Media Media, MediaSourceLink Link)> filtered = all;

        if (since is not null)
        {
            filtered = filtered.Where(t => TryGetCreationDate(t.Media) is { } d && d >= since.Value);
        }

        if (takeRecent is not null)
        {
            filtered = filtered.OrderByDescending(t => t.Link.SortNumber).Take(takeRecent.Value);
        }

        return filtered.ToList();
    }

    private static DateTime? TryGetCreationDate(Media media)
    {
        var raw = media.Metadata.FirstOrDefault(m => m.Key == "CreationDate")?.Value;

        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return DateTime.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private sealed class FetchCounters
    {
        public int Ok;
        public int Failed;
        public int Processed;
    }
}

internal sealed record CommentsFetchProgress(int Processed, int Total, string Message, string Counter);
