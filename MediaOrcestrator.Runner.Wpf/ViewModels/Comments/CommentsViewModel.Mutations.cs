using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed partial class CommentsViewModel
{
    public Task ExecuteLikeAsync(CommentItemViewModel item, bool liked)
    {
        return RunMutationAsync(
            item,
            liked ? CommentMutationKind.Like : CommentMutationKind.Unlike,
            async (source, link, ct) =>
            {
                var newCount = await _commentsService.LikeCommentAsync(source, link, item.Record.ExternalCommentId, liked, ct);
                _dispatcher.Invoke(() => item.ApplyLikeUpdate(liked, newCount));
            });
    }

    public async Task<string?> ExecuteEditAsync(CommentItemViewModel item, string newText)
    {
        string? finalText = null;

        await RunMutationAsync(item, CommentMutationKind.Edit, async (source, link, ct) =>
        {
            var updated = await _commentsService.EditCommentAsync(source, link, item.Record.ExternalCommentId, newText, ct);
            finalText = updated.Text;
        });

        return finalText;
    }

    public Task ExecuteDeleteAsync(CommentItemViewModel item)
    {
        return RunMutationAsync(item, CommentMutationKind.Delete, async (source, link, ct) =>
        {
            await _commentsService.DeleteCommentAsync(source, link, item.Record.ExternalCommentId, ct);
        });
    }

    public Task ExecuteRestoreAsync(CommentItemViewModel item)
    {
        return RunMutationAsync(item, CommentMutationKind.Restore, async (source, link, ct) =>
        {
            await _commentsService.RestoreCommentAsync(source, link, item.Record.ExternalCommentId, ct);
        });
    }

    public void StartReply(CommentItemViewModel target)
    {
        if (_activeReplyEditor is not null)
        {
            CancelReply(_activeReplyEditor);
        }

        var prefix = BuildReplyPrefix(target.AuthorName);

        var editor = new ReplyEditorViewModel(
            target.Record.SourceId,
            target.Record.ExternalMediaId,
            target.Record.ExternalCommentId,
            target.Depth + 1,
            target.HasAuthors,
            prefix,
            SubmitReplyAsync,
            CancelReply);

        _activeReplyEditor = editor;

        var index = Items.IndexOf(target);

        if (index < 0)
        {
            return;
        }

        Items.Insert(index + 1, editor);

        if (target.HasAuthors)
        {
            _ = LoadAuthorsAsync(target.Record.SourceId, target.Record.ExternalMediaId, editor);
        }
    }

    public void OpenCommentInSource(CommentItemViewModel item)
    {
        var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == item.Record.SourceId);

        if (source?.Type is not ISupportsCommentPermalinks permalinks)
        {
            return;
        }

        try
        {
            var allComments = _commentsService.GetByMedia(item.Record.SourceId, item.Record.ExternalMediaId);
            var rootId = FindRootCommentId(allComments, item.Record.ExternalCommentId);

            var uri = permalinks.GetCommentExternalUri(
                item.Record.ExternalMediaId,
                item.Record.ExternalCommentId,
                rootId,
                source.Settings,
                null);

            if (uri is null)
            {
                return;
            }

            if (!_shellLauncher.Open(uri.ToString()))
            {
                _logger.LogError("Не удалось открыть комментарий в источнике");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось открыть комментарий в источнике");
        }
    }

    public void OpenMediaInSource(CommentItemViewModel item)
    {
        var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == item.Record.SourceId);

        if (source?.Type is null)
        {
            return;
        }

        try
        {
            var uri = source.Type.GetExternalUri(item.Record.ExternalMediaId, source.Settings);

            if (uri is null)
            {
                return;
            }

            if (!_shellLauncher.Open(uri.ToString()))
            {
                _logger.LogError("Не удалось открыть медиа в источнике");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось открыть медиа в источнике");
        }
    }

    public void OpenExternalLink(Uri uri)
    {
        if (!_shellLauncher.Open(uri.ToString()))
        {
            _logger.LogError("Не удалось открыть ссылку из комментария {Uri}", uri);
        }
    }

    private async Task SubmitReplyAsync(ReplyEditorViewModel editor)
    {
        var text = (editor.Text ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var (source, media, link) = ResolveCommentTarget(editor.SourceId, editor.ExternalMediaId,
            "Отправка комментария", requireCreate: true);

        using var cts = new CancellationTokenSource();
        var action = _actionHolder.Register(
            $"Отправка комментария: «{media.Title}»",
            "Запущена",
            1,
            cts,
            kind: ActionKind.Comments);

        try
        {
            await _commentsService.CreateCommentAsync(source, link, editor.ParentExternalCommentId, text, editor.SelectedAuthor?.Id, cts.Token);
            action.ProgressPlus();
            action.Finish();
            InvalidateSnapshotCache();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            action.MarkCancelled("Отменено");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось отправить комментарий к {MediaId} в источник {SourceId}", media.Id, source.Id);
            action.Fail("Ошибка: " + ex.Message, ex);
            throw;
        }

        CancelReply(editor);
        ScheduleApplyFilters();
    }

    private void CancelReply(ReplyEditorViewModel editor)
    {
        if (_activeReplyEditor == editor)
        {
            _activeReplyEditor = null;
        }

        Items.Remove(editor);
    }

    private async Task LoadAuthorsAsync(string sourceId, string externalMediaId, ReplyEditorViewModel editor)
    {
        var key = (sourceId, externalMediaId);

        if (_authorsCache.TryGetValue(key, out var cached))
        {
            editor.ReplaceAuthors(cached);
            return;
        }

        if (!_authorsInFlight.Add(key))
        {
            return;
        }

        editor.IsAuthorsLoading = true;

        try
        {
            var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == sourceId);

            if (source?.Type is not ISupportsCommentAuthors)
            {
                return;
            }

            var link = _orcestrator.GetMedias()
                .SelectMany(m => m.Sources)
                .FirstOrDefault(l => l.SourceId == sourceId && l.ExternalId == externalMediaId);

            if (link is null)
            {
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var raw = await _commentsService.GetAvailableAuthorsAsync(source, link, cts.Token);
            var list = raw.Select(a => new CommentAuthorOptionViewModel(a.Id, a.Name, a.AvatarUrl, a.IsDefault, _dispatcher)).ToList();

            _authorsCache[key] = list;
            _dispatcher.Invoke(() => editor.ReplaceAuthors(list));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить список авторов для {Source}/{Media}", sourceId, externalMediaId);
        }
        finally
        {
            editor.IsAuthorsLoading = false;
            _authorsInFlight.Remove(key);
        }
    }

    private string BuildReplyPrefix(string authorName)
    {
        var template = string.IsNullOrEmpty(_settings.ReplyPrefixTemplate) ? "{name}, " : _settings.ReplyPrefixTemplate;
        return template.Replace("{name}", authorName);
    }

    private static string? FindRootCommentId(IReadOnlyList<CommentRecord> comments, string externalCommentId)
    {
        var byId = new Dictionary<string, CommentRecord>(comments.Count, StringComparer.Ordinal);

        foreach (var c in comments)
        {
            byId.TryAdd(c.ExternalCommentId, c);
        }

        if (!byId.TryGetValue(externalCommentId, out var current) || string.IsNullOrEmpty(current.ParentExternalCommentId))
        {
            return null;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var rootId = current.ParentExternalCommentId;

        while (byId.TryGetValue(rootId, out var parent)
               && !string.IsNullOrEmpty(parent.ParentExternalCommentId)
               && visited.Add(rootId))
        {
            rootId = parent.ParentExternalCommentId;
        }

        return rootId;
    }

    private async Task RunMutationAsync(CommentItemViewModel item, CommentMutationKind kind, Func<Source, MediaSourceLink, CancellationToken, Task> body)
    {
        var label = kind switch
        {
            CommentMutationKind.Edit => "Изменение комментария",
            CommentMutationKind.Delete => "Удаление комментария",
            CommentMutationKind.Restore => "Восстановление комментария",
            CommentMutationKind.Like => "Лайк комментария",
            CommentMutationKind.Unlike => "Снятие лайка",
            _ => "Комментарий",
        };
        var (source, media, link) = ResolveCommentTarget(item.Record.SourceId, item.Record.ExternalMediaId, label);

        using var cts = new CancellationTokenSource();
        var action = _actionHolder.Register(
            $"{label}: «{media.Title}»",
            "Запущена",
            1,
            cts,
            kind: ActionKind.Comments);

        try
        {
            await body(source, link, cts.Token);
            action.Status = $"{source.TitleFull}: ок";
            action.ProgressPlus();
            action.Finish();
            InvalidateSnapshotCache();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            action.MarkCancelled("Отменено");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Операция {Operation} для комментария {CommentId} источника {SourceId} завершилась ошибкой",
                label, item.Record.ExternalCommentId, source.Id);
            action.Fail("Ошибка: " + ex.Message, ex);
            throw;
        }

        if (kind is CommentMutationKind.Delete or CommentMutationKind.Restore)
        {
            ScheduleApplyFilters();
        }
    }

    private (Source Source, Media Media, MediaSourceLink Link) ResolveCommentTarget(string sourceId, string externalMediaId,
        string operation, bool requireCreate = false)
    {
        try
        {
            var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == sourceId);
            if (requireCreate && source?.Type is not ISupportsCommentMutations)
            {
                throw new InvalidOperationException("Источник не поддерживает создание комментариев.");
            }
            if (source is null)
            {
                throw new InvalidOperationException("Источник не найден.");
            }

            foreach (var media in _orcestrator.GetMedias())
            {
                var link = media.Sources.FirstOrDefault(l => l.SourceId == sourceId && l.ExternalId == externalMediaId);
                if (link is not null)
                {
                    return (source, media, link);
                }
            }

            throw new InvalidOperationException("Медиа не найдено локально.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось подготовить операцию {Operation} для видео {ExternalMediaId} источника {SourceId}",
                operation, externalMediaId, sourceId);
            throw;
        }
    }

    private enum CommentMutationKind
    {
        None = 0,
        Edit = 1,
        Delete = 2,
        Restore = 3,
        Like = 4,
        Unlike = 5,
    }
}
