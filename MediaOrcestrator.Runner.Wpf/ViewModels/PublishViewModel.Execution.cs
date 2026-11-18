using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class PublishViewModel
{
    [RelayCommand(CanExecute = nameof(CanPublishOrCancel))]
    private async Task PublishAsync()
    {
        if (IsPublishing)
        {
            SetStatus("Отмена…", false);

            if (_publishCts is { } cts)
            {
                await cts.CancelAsync();
            }

            return;
        }

        if (SelectedSource is not { } source)
        {
            SetStatus("Выберите источник.", true);
            return;
        }

        var title = Title.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            SetStatus("Укажите название.", true);
            return;
        }

        if (string.IsNullOrEmpty(VideoPath) || !File.Exists(VideoPath))
        {
            SetStatus("Выберите видеофайл.", true);
            return;
        }

        IsPublishing = true;
        AuthenticationRecovery.Failures.Clear();
        var pubCts = new CancellationTokenSource();
        _publishCts = pubCts;
        var token = pubCts.Token;

        SetStatus($"Публикация в «{source.TitleFull}»…", false);

        var description = Description;
        var videoPath = VideoPath;
        var coverPath = CoverPath;

        var pubAction = _actionHolder.Register(
            $"Публикация: «{title}» ({source.TitleFull})",
            "Публикация…",
            0,
            pubCts,
            kind: ActionKind.Publish);

        try
        {
            Media published;
            using (_actionHolder.BeginScope(pubAction))
            {
                published = await Task.Run(
                    () => _orcestrator.PublishMediaAsync(source, title, description, videoPath, coverPath, token),
                    token);
            }

            pubAction.Finish("Опубликовано");
            SetStatus($"Опубликовано в «{source.TitleFull}».", false);
            _logger.LogInformation("Медиа «{Title}» опубликовано в {Source}", title, source.TitleFull);
            InvalidateMediasCache();

            if (RunChainAfterPublish)
            {
                await RunPostPublishChainAsync(published, source, token);
            }

            ResetForm();
        }
        catch (OperationCanceledException)
        {
            pubAction.MarkCancelled("Публикация отменена");
            _logger.LogInformation("Публикация «{Title}» отменена пользователем", title);
            SetStatus("Публикация отменена.", false);
        }
        catch (Exception exception)
        {
            AuthenticationRecovery.Report(exception);
            pubAction.Fail("Ошибка: " + exception.Message, exception);
            _logger.LogError(exception, "Ошибка при публикации «{Title}» в {Source}", title, source.TitleFull);
            SetStatus($"Ошибка: {exception.Message}", true);
        }
        finally
        {
            IsPublishing = false;

            if (ReferenceEquals(_publishCts, pubCts))
            {
                _publishCts = null;
            }

            pubCts.Dispose();
        }
    }

    private bool CanPublishOrCancel()
    {
        if (IsPublishing)
        {
            return true;
        }

        return SelectedSource is not null
               && !string.IsNullOrWhiteSpace(Title)
               && !string.IsNullOrEmpty(VideoPath);
    }

    private async Task RunPostPublishChainAsync(Media media, Source publishedSource, CancellationToken cancellationToken)
    {
        var relations = _orcestrator.GetRelations();
        var roots = _syncPlanner.Plan([media], relations);
        var chain = EnumerateChain(roots).ToList();

        if (chain.Count == 0)
        {
            return;
        }

        using var chainCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var chainToken = chainCts.Token;

        var actionName = $"Цепочка после публикации: «{media.Title}» от {publishedSource.TitleFull}";
        var running = _actionHolder.Register(actionName, "В процессе", chain.Count, chainCts, kind: ActionKind.Sync);

        using var chainScope = _actionHolder.BeginScope(running);

        SyncIntent? failedIntent = null;

        try
        {
            for (var i = 0; i < chain.Count; i++)
            {
                chainToken.ThrowIfCancellationRequested();

                var intent = chain[i];
                var step = $"{i + 1}/{chain.Count}";
                var stepLabel = $"{intent.From.TitleFull} → {intent.To.TitleFull}";
                SetStatus($"Цепочка {step}: {stepLabel}…", false);
                _actionHolder.SetStatus(running.Id, $"{step}: {stepLabel}");

                failedIntent = intent;
                var outcome = await _retryRunner.RunAsync(intent.Media, intent.Relation, cancellationToken: chainToken);
                failedIntent = null;

                if (outcome == SyncRunOutcome.Skipped)
                {
                    _logger.LogInformation("Цепочка: «{Title}» пропущена на {From} → {To}",
                        intent.Media.Title, intent.From.TitleFull, intent.To.TitleFull);
                    SetStatus($"Цепочка остановлена: {stepLabel} пропущено.", false);
                    running.Finish("Пропущено");
                    return;
                }

                _logger.LogInformation("Цепочка: «{Title}» прошла {From} → {To}",
                    intent.Media.Title, intent.From.TitleFull, intent.To.TitleFull);

                running.ProgressPlus();
            }

            SetStatus($"Цепочка завершена ({chain.Count} шаг(ов)).", false);
            running.Finish();
        }
        catch (OperationCanceledException)
        {
            running.MarkCancelled();
            throw;
        }
        catch (Exception exception) when (failedIntent is not null)
        {
            AuthenticationRecovery.Report(exception);
            _logger.LogError(exception, "Цепочка: «{Title}» сорвалась на {From} → {To}",
                media.Title, failedIntent.From.TitleFull, failedIntent.To.TitleFull);

            SetStatus($"Цепочка прервана на {failedIntent.To.TitleFull}: {exception.Message}", true);
            running.Fail($"Ошибка на {failedIntent.To.TitleFull}", exception);
        }
    }

    private static IEnumerable<SyncIntent> EnumerateChain(List<SyncIntent> intents)
    {
        foreach (var intent in intents.Where(x => x.IsSelected))
        {
            yield return intent;

            foreach (var child in EnumerateChain(intent.NextIntents))
            {
                yield return child;
            }
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    private void InvalidateMediasCache()
    {
        _isMediasCacheValid = false;
        _mediasCache.Clear();
    }

    private void TryAutoFillDescriptionFromTitle(string? title)
    {
        if (string.IsNullOrEmpty(title)
            || !string.IsNullOrWhiteSpace(Description)
            || !_titleToDescription.TryGetValue(title, out var description))
        {
            return;
        }

        Description = description;
    }

    private async Task UpdateSidecarDescriptionSafeAsync(string? videoPath)
    {
        try
        {
            await UpdateSidecarDescriptionAsync(videoPath);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось обновить описание из файла рядом с видео {Path}", videoPath);
        }
    }

    private async Task UpdateSidecarDescriptionAsync(string? videoPath)
    {
        _sidecarCts?.Cancel();
        var cts = new CancellationTokenSource();
        _sidecarCts = cts;

        string? sidecar;

        if (string.IsNullOrEmpty(videoPath))
        {
            sidecar = null;
        }
        else
        {
            try
            {
                sidecar = await Task.Run(() => PublishSuggestionHelper.TryReadSidecarDescription(videoPath, _logger), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (cts.IsCancellationRequested || !ReferenceEquals(_sidecarCts, cts))
        {
            return;
        }

        _sidecarCts = null;
        cts.Dispose();

        if (string.Equals(_videoSidecarDescription, sidecar, StringComparison.Ordinal))
        {
            return;
        }

        for (var i = DescriptionTemplates.Count - 1; i >= 0; i--)
        {
            if (DescriptionTemplates[i].IsFromVideoFile)
            {
                DescriptionTemplates.RemoveAt(i);
            }
        }

        _videoSidecarDescription = sidecar;

        if (sidecar is null)
        {
            return;
        }

        DescriptionTemplates.Insert(0, new(sidecar, isFromVideoFile: true));

        if (string.IsNullOrWhiteSpace(Description))
        {
            Description = sidecar;
        }
    }

    private async Task ReloadSourceSuggestionsAsync()
    {
        _suggestionsCts?.Cancel();
        _suggestionsCts = null;

        ClearSuggestions();

        if (SelectedSource is not { } source)
        {
            return;
        }

        var sourceId = source.Id;

        if (!_isMediasCacheValid)
        {
            var cts = new CancellationTokenSource();
            _suggestionsCts = cts;
            var token = cts.Token;

            try
            {
                var medias = await Task.Run(() => _orcestrator.GetMedias(), token);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                _mediasCache.Clear();
                _mediasCache.AddRange(medias);
                _isMediasCacheValid = true;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Не удалось загрузить историю публикаций для {SourceId}", sourceId);
                return;
            }
            finally
            {
                if (ReferenceEquals(_suggestionsCts, cts))
                {
                    _suggestionsCts = null;
                }

                cts.Dispose();
            }
        }

        if (SelectedSource?.Id != sourceId)
        {
            return;
        }

        var suggestions = PublishSuggestionHelper.BuildForSource(_mediasCache, sourceId, _videoSidecarDescription);
        TitleSuggestions.AddRange(suggestions.Titles);

        foreach (var (titleKey, desc) in suggestions.TitleToDescription)
        {
            _titleToDescription.TryAdd(titleKey, desc);
        }

        DescriptionTemplates.AddRange(suggestions.Descriptions);
    }

    private void ClearSuggestions()
    {
        TitleSuggestions.Clear();
        _titleToDescription.Clear();

        for (var i = DescriptionTemplates.Count - 1; i >= 0; i--)
        {
            if (!DescriptionTemplates[i].IsFromVideoFile)
            {
                DescriptionTemplates.RemoveAt(i);
            }
        }
    }
}
