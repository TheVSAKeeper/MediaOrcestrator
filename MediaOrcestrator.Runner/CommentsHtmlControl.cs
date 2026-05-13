using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Timer = System.Windows.Forms.Timer;

namespace MediaOrcestrator.Runner;

public partial class CommentsHtmlControl : UserControl
{
    private const string AnySourceLabel = "(любой)";
    private const int SearchDebounceMs = 300;

    private static readonly TimeSpan AuthorsCacheTtl = TimeSpan.FromMinutes(10);

    private readonly Timer _searchDebounce = new() { Interval = SearchDebounceMs };

    private readonly ConcurrentDictionary<(string SourceId, string ExternalMediaId), (DateTime FetchedAt, IReadOnlyList<CommentAuthorView> Authors)> _authorsCache = new();

    private Orcestrator? _orcestrator;
    private CommentsService? _commentsService;
    private ActionHolder? _actionHolder;
    private ILogger? _logger;
    private CommentsViewSettings _settings = new();
    private bool _invertMediaSort;
    private bool _invertCommentSort;
    private bool _loaded;
    private bool _suppressSettingsSave;
    private int _applyFiltersVersion;
    private CancellationTokenSource? _applyFiltersCts;

    public CommentsHtmlControl()
    {
        InitializeComponent();
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            ApplyFilters();
        };

        uiBrowserView.MediaRequested += (_, mediaId) => OpenMediaById(mediaId);
        uiBrowserView.FetchRequested += async (_, req) =>
        {
            try
            {
                await FetchForExternalAsync(req.SourceId, req.ExternalId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Не удалось обработать app-ссылку fetch ({Source}/{External})",
                    req.SourceId, req.ExternalId);
            }
        };

        uiBrowserView.OpenExternalRequested += (_, req) => OpenExternal(req.SourceId, req.ExternalId);
        uiBrowserView.OpenCommentExternalRequested += (_, req) => OpenCommentExternal(req.SourceId, req.ExternalMediaId, req.ExternalCommentId);

        uiBrowserView.MutationRequested += async (_, req) =>
        {
            try
            {
                await HandleMutationAsync(req);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Не удалось обработать операцию {Kind} ({Source}/{Media}/{Comment})",
                    req.Kind, req.SourceId, req.ExternalMediaId, req.ExternalCommentId);

                MessageBox.Show($"Не удалось выполнить операцию: {ex.Message}",
                    "Ошибка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                ApplyFilters();
            }
        };
    }

    public void Initialize(
        Orcestrator orcestrator,
        CommentsService commentsService,
        ActionHolder actionHolder,
        ILogger logger)
    {
        _orcestrator = orcestrator;
        _commentsService = commentsService;
        _actionHolder = actionHolder;
        _logger = logger;

        _settings = CommentsViewSettings.Load();

        uiBrowserView.AuthorsResolver = ResolveCommentAuthors;

        using (Splash.Current.StartSpan("Сортировки"))
        {
            PopulateSortCombos();
        }

        using (Splash.Current.StartSpan("Список источников"))
        {
            ReloadSourcesCombo();
        }

        ApplySettingsToUi();

        using (Splash.Current.StartSpan("Прогрев браузера"))
        {
            uiBrowserView.Prewarm();
        }

        UpdateForceFetchButtonState();
    }

    public void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        ApplyFilters();
    }

    private void uiSortChanged(object? sender, EventArgs e)
    {
        SaveSettings();
        ApplyFilters();
    }

    private void uiRefreshButton_Click(object? sender, EventArgs e)
    {
        ApplyFilters();
    }

    private void uiSourceComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        SaveSettings();
        UpdateForceFetchButtonState();
        ApplyFilters();
    }

    private void uiSearchTextBox_TextChanged(object? sender, EventArgs e)
    {
        SaveSettings();
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void uiMediaSearchTextBox_TextChanged(object? sender, EventArgs e)
    {
        SaveSettings();
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void uiSearchTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        _searchDebounce.Stop();
        ApplyFilters();
    }

    private void uiMediaSortInvertButton_Click(object? sender, EventArgs e)
    {
        _invertMediaSort = !_invertMediaSort;
        uiMediaSortInvertButton.Text = _invertMediaSort ? "↑" : "↓";
        SaveSettings();
        ApplyFilters();
    }

    private void uiCommentSortInvertButton_Click(object? sender, EventArgs e)
    {
        _invertCommentSort = !_invertCommentSort;
        uiCommentSortInvertButton.Text = _invertCommentSort ? "↑" : "↓";
        SaveSettings();
        ApplyFilters();
    }

    private void uiFetchSettingsValueChanged(object? sender, EventArgs e)
    {
        SaveSettings();
    }

    private async void uiForceFetchAllButton_Click(object? sender, EventArgs e)
    {
        if (_orcestrator == null || _commentsService == null || _actionHolder == null)
        {
            return;
        }

        if (uiSourceComboBox.SelectedItem is not SourceComboItem { SourceId: { } sourceId })
        {
            return;
        }

        var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == sourceId);
        if (source == null)
        {
            return;
        }

        await FetchAllForSourceAsync(source);
    }

    private static void SelectComboValue<T>(ComboBox combo, T value) where T : Enum
    {
        if (combo.DataSource is not List<SortItem<T>> items)
        {
            return;
        }

        var match = items.FirstOrDefault(x => x.Value.Equals(value));
        if (match != null)
        {
            combo.SelectedItem = match;
        }
    }

    private static void BackfillOrphanParents(List<CommentRecord> fetched, CommentsService commentsService, CancellationToken ct)
    {
        if (fetched.Count == 0)
        {
            return;
        }

        var known = new HashSet<string>(fetched.Select(r => r.Id), StringComparer.Ordinal);
        var pending = new Queue<string>();

        foreach (var r in fetched)
        {
            if (string.IsNullOrEmpty(r.ParentExternalCommentId))
            {
                continue;
            }

            var parentId = $"{r.SourceId}|{r.ExternalMediaId}|{r.ParentExternalCommentId}";

            if (known.Add(parentId))
            {
                pending.Enqueue(parentId);
            }
        }

        var safetyLimit = fetched.Count * 2;

        while (pending.Count > 0 && safetyLimit-- > 0)
        {
            ct.ThrowIfCancellationRequested();

            var parentId = pending.Dequeue();
            var parent = commentsService.GetById(parentId);

            if (parent == null)
            {
                continue;
            }

            fetched.Add(parent);

            if (string.IsNullOrEmpty(parent.ParentExternalCommentId))
            {
                continue;
            }

            var grandparentId = $"{parent.SourceId}|{parent.ExternalMediaId}|{parent.ParentExternalCommentId}";

            if (known.Add(grandparentId))
            {
                pending.Enqueue(grandparentId);
            }
        }
    }

    private static string BuildFilterSummary(int? sinceDays, int? takeRecent, int? staleDays)
    {
        var parts = new List<string>(3);

        if (sinceDays != null)
        {
            parts.Add($"медиа не старше {sinceDays.Value} дн.");
        }

        if (takeRecent != null)
        {
            parts.Add($"только последние {takeRecent.Value}");
        }

        if (staleDays != null)
        {
            parts.Add($"не обновлялись > {staleDays.Value} дн.");
        }

        return string.Join(", ", parts);
    }

    private static DateTime? TryGetCreationDate(Media media)
    {
        var raw = media.Metadata.FirstOrDefault(m => m.Key == "CreationDate")?.Value;
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return DateTime.TryParse(raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? FindRootCommentId(IReadOnlyList<CommentRecord> comments, string externalCommentId)
    {
        var byId = comments.ToDictionary(c => c.ExternalCommentId);
        if (!byId.TryGetValue(externalCommentId, out var current))
        {
            return null;
        }

        if (string.IsNullOrEmpty(current.ParentExternalCommentId))
        {
            return null;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var rootId = current.ParentExternalCommentId;

        while (byId.TryGetValue(rootId!, out var parent)
               && !string.IsNullOrEmpty(parent.ParentExternalCommentId)
               && visited.Add(rootId!))
        {
            rootId = parent.ParentExternalCommentId;
        }

        return rootId;
    }

    private void ApplySettingsToUi()
    {
        _suppressSettingsSave = true;
        try
        {
            uiSearchTextBox.Text = _settings.Search;
            uiMediaSearchTextBox.Text = _settings.MediaSearch;
            uiLimitNumeric.Value = Math.Clamp(_settings.Limit, (int)uiLimitNumeric.Minimum, (int)uiLimitNumeric.Maximum);
            uiFetchSinceDaysNumeric.Value = Math.Clamp(_settings.FetchSinceDays, (int)uiFetchSinceDaysNumeric.Minimum, (int)uiFetchSinceDaysNumeric.Maximum);
            uiFetchOnlyRecentNumeric.Value = Math.Clamp(_settings.FetchOnlyRecent, (int)uiFetchOnlyRecentNumeric.Minimum, (int)uiFetchOnlyRecentNumeric.Maximum);
            uiFetchStaleNumeric.Value = Math.Clamp(_settings.FetchStaleDays, (int)uiFetchStaleNumeric.Minimum, (int)uiFetchStaleNumeric.Maximum);

            _invertMediaSort = _settings.InvertMediaSort;
            uiMediaSortInvertButton.Text = _invertMediaSort ? "↑" : "↓";
            _invertCommentSort = _settings.InvertCommentSort;
            uiCommentSortInvertButton.Text = _invertCommentSort ? "↑" : "↓";

            SelectComboValue(uiMediaSortComboBox, _settings.MediaSort);
            SelectComboValue(uiCommentSortComboBox, _settings.CommentSort);

            if (!string.IsNullOrEmpty(_settings.SelectedSourceId)
                && uiSourceComboBox.DataSource is List<SourceComboItem> items
                && items.FirstOrDefault(x => x.SourceId == _settings.SelectedSourceId) is { } match)
            {
                uiSourceComboBox.SelectedItem = match;
            }
        }
        finally
        {
            _suppressSettingsSave = false;
        }
    }

    private void SaveSettings()
    {
        if (_suppressSettingsSave)
        {
            return;
        }

        _settings.SelectedSourceId = (uiSourceComboBox.SelectedItem as SourceComboItem)?.SourceId;
        _settings.Search = uiSearchTextBox.Text;
        _settings.MediaSearch = uiMediaSearchTextBox.Text;
        _settings.Limit = (int)uiLimitNumeric.Value;
        _settings.FetchSinceDays = (int)uiFetchSinceDaysNumeric.Value;
        _settings.FetchOnlyRecent = (int)uiFetchOnlyRecentNumeric.Value;
        _settings.FetchStaleDays = (int)uiFetchStaleNumeric.Value;
        _settings.InvertMediaSort = _invertMediaSort;
        _settings.InvertCommentSort = _invertCommentSort;
        _settings.MediaSort = (uiMediaSortComboBox.SelectedItem as SortItem<MediaSort>)?.Value ?? MediaSort.ByTitle;
        _settings.CommentSort = (uiCommentSortComboBox.SelectedItem as SortItem<CommentSort>)?.Value ?? CommentSort.ByDate;

        _settings.Save();
    }

    private void PopulateSortCombos()
    {
        uiMediaSortComboBox.BeginUpdate();
        try
        {
            uiMediaSortComboBox.DataSource = new List<SortItem<MediaSort>>
            {
                new(MediaSort.ByTitle, "По названию"),
                new(MediaSort.BySortNumber, "По порядку в источнике"),
            };

            uiMediaSortComboBox.DisplayMember = nameof(SortItem<MediaSort>.Label);
            uiMediaSortComboBox.SelectedIndex = 0;
        }
        finally
        {
            uiMediaSortComboBox.EndUpdate();
        }

        uiCommentSortComboBox.BeginUpdate();
        try
        {
            uiCommentSortComboBox.DataSource = new List<SortItem<CommentSort>>
            {
                new(CommentSort.ByDate, "По дате"),
                new(CommentSort.ByLikes, "По лайкам"),
                new(CommentSort.ByReplies, "По числу ответов"),
                new(CommentSort.WithoutAuthorReply, "Без ответа автора"),
            };

            uiCommentSortComboBox.DisplayMember = nameof(SortItem<CommentSort>.Label);
            uiCommentSortComboBox.SelectedIndex = 0;
        }
        finally
        {
            uiCommentSortComboBox.EndUpdate();
        }
    }

    private void ReloadSourcesCombo()
    {
        if (_orcestrator == null)
        {
            return;
        }

        var previousValue = uiSourceComboBox.SelectedItem as SourceComboItem;
        var sources = _orcestrator.GetSources();
        var items = new List<SourceComboItem>
        {
            new(null, AnySourceLabel),
        };

        foreach (var source in sources.OrderBy(s => s.TitleFull))
        {
            items.Add(new(source.Id, source.TitleFull));
        }

        uiSourceComboBox.BeginUpdate();
        try
        {
            uiSourceComboBox.DataSource = items;
            uiSourceComboBox.DisplayMember = nameof(SourceComboItem.Label);
            uiSourceComboBox.ValueMember = nameof(SourceComboItem.SourceId);

            if (previousValue?.SourceId != null
                && items.FirstOrDefault(x => x.SourceId == previousValue.SourceId) is { } match)
            {
                uiSourceComboBox.SelectedItem = match;
            }
            else
            {
                uiSourceComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            uiSourceComboBox.EndUpdate();
        }
    }

    private async void ApplyFilters()
    {
        if (!_loaded || _orcestrator == null || _commentsService == null)
        {
            return;
        }

        var version = ++_applyFiltersVersion;

        _applyFiltersCts?.Cancel();
        _applyFiltersCts?.Dispose();
        _applyFiltersCts = new();
        var ct = _applyFiltersCts.Token;

        var orcestrator = _orcestrator;
        var commentsService = _commentsService;
        var sourceId = (uiSourceComboBox.SelectedItem as SourceComboItem)?.SourceId;
        var search = uiSearchTextBox.Text.Trim();
        var mediaSearch = uiMediaSearchTextBox.Text.Trim();
        var limit = (int)uiLimitNumeric.Value;
        var mediaSort = (uiMediaSortComboBox.SelectedItem as SortItem<MediaSort>)?.Value ?? MediaSort.ByTitle;
        var commentSort = (uiCommentSortComboBox.SelectedItem as SortItem<CommentSort>)?.Value ?? CommentSort.ByDate;
        var invertMediaSort = _invertMediaSort;
        var invertCommentSort = _invertCommentSort;

        uiStatusLabel.Text = "Загрузка...";

        try
        {
            var (records, json, truncated) = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var fetched = commentsService.Query(sourceId,
                    textContains: string.IsNullOrEmpty(search) ? null : search,
                    limit: limit + 1);

                ct.ThrowIfCancellationRequested();

                var isTruncated = fetched.Count > limit;
                if (isTruncated)
                {
                    fetched = fetched.Take(limit).ToList();
                }

                BackfillOrphanParents(fetched, commentsService, ct);

                var renderJson = CommentsBrowserView.BuildRenderJson(orcestrator, fetched, new()
                {
                    Search = search,
                    CommentSort = commentSort,
                    InvertCommentSort = invertCommentSort,
                    MediaSort = mediaSort,
                    InvertMediaSort = invertMediaSort,
                    MediaSearch = mediaSearch,
                });

                return (Records: fetched, Json: renderJson, Truncated: isTruncated);
            }, ct);

            if (version != _applyFiltersVersion || IsDisposed)
            {
                return;
            }

            try
            {
                uiBrowserView.ApplyJson(json);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            WarmAuthors(records);

            uiStatusLabel.Text = records.Count == 0
                ? "Ничего не найдено"
                : truncated
                    ? $"Комментариев: ≥{records.Count} (увеличьте лимит)"
                    : $"Комментариев: {records.Count}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (version != _applyFiltersVersion || IsDisposed)
            {
                return;
            }

            _logger?.LogError(ex, "Не удалось загрузить комментарии");
            uiStatusLabel.Text = "Ошибка загрузки";
            MessageBox.Show($"Не удалось загрузить комментарии: {ex.Message}",
                "Ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void UpdateForceFetchButtonState()
    {
        var sourceItem = uiSourceComboBox.SelectedItem as SourceComboItem;
        uiForceFetchAllButton.Enabled = sourceItem?.SourceId != null;
    }

    private void OpenMediaById(string mediaId)
    {
        if (_orcestrator == null || _actionHolder == null)
        {
            return;
        }

        var media = _orcestrator.GetMedias().FirstOrDefault(m => m.Id == mediaId);
        if (media == null)
        {
            return;
        }

        var detail = new MediaDetailForm(media, _orcestrator, _actionHolder, _commentsService, _logger);
        detail.Show(this);
    }

    private void OpenExternal(string sourceId, string externalId)
    {
        if (_orcestrator == null)
        {
            return;
        }

        var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == sourceId);
        if (source?.Type == null)
        {
            return;
        }

        try
        {
            var metadata = ResolveMediaMetadata(sourceId, externalId);
            var uri = source.Type.GetExternalUri(externalId, source.Settings, metadata);
            if (uri == null)
            {
                return;
            }

            Process.Start(new ProcessStartInfo(uri.ToString())
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Не удалось открыть медиа в источнике ({Source}/{External})",
                sourceId, externalId);
        }
    }

    private async void OpenCommentExternal(string sourceId, string externalMediaId, string externalCommentId)
    {
        if (_orcestrator == null || _commentsService == null)
        {
            return;
        }

        var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == sourceId);
        if (source?.Type is not ISupportsCommentPermalinks permalinks)
        {
            return;
        }

        var commentsService = _commentsService;
        var rootCommentId = await Task.Run(() =>
        {
            var allComments = commentsService.GetByMedia(sourceId, externalMediaId);
            return FindRootCommentId(allComments, externalCommentId);
        });

        if (IsDisposed)
        {
            return;
        }

        try
        {
            var metadata = ResolveMediaMetadata(sourceId, externalMediaId);
            var uri = permalinks.GetCommentExternalUri(externalMediaId,
                externalCommentId,
                rootCommentId,
                source.Settings,
                metadata);

            if (uri == null)
            {
                return;
            }

            Process.Start(new ProcessStartInfo(uri.ToString())
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Не удалось открыть комментарий в источнике ({Source}/{Media}/{Comment})",
                sourceId, externalMediaId, externalCommentId);
        }
    }

    private IReadOnlyList<MetadataItem>? ResolveMediaMetadata(string sourceId, string externalMediaId)
    {
        if (_orcestrator == null)
        {
            return null;
        }

        foreach (var media in _orcestrator.GetMedias())
        {
            foreach (var link in media.Sources)
            {
                if (link.SourceId == sourceId && link.ExternalId == externalMediaId)
                {
                    return media.Metadata.ForSource(sourceId);
                }
            }
        }

        return null;
    }

    private async Task HandleMutationAsync(CommentMutationRequest request)
    {
        if (_orcestrator == null || _commentsService == null || _actionHolder == null)
        {
            return;
        }

        var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == request.SourceId);
        if (source == null)
        {
            return;
        }

        var isLikeOp = request.Kind is CommentMutationKind.Like or CommentMutationKind.Unlike;
        var supportsRequiredFeature = isLikeOp
            ? source.Type is ISupportsCommentLikes
            : source.Type is ISupportsCommentMutations;

        if (!supportsRequiredFeature)
        {
            MessageBox.Show(isLikeOp
                    ? "Источник не поддерживает лайки на комментариях."
                    : "Источник не поддерживает изменение комментариев.",
                "Операция недоступна",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        var media = _orcestrator.GetMedias()
            .FirstOrDefault(m => m.Sources.Any(l => l.SourceId == request.SourceId && l.ExternalId == request.ExternalMediaId));

        var link = media?.Sources.FirstOrDefault(l => l.SourceId == request.SourceId && l.ExternalId == request.ExternalMediaId);
        if (media == null || link == null)
        {
            MessageBox.Show("Медиа не найдено локально.",
                "Операция недоступна",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        var label = request.Kind switch
        {
            CommentMutationKind.Create => "Отправка комментария",
            CommentMutationKind.Edit => "Изменение комментария",
            CommentMutationKind.Delete => "Удаление комментария",
            CommentMutationKind.Restore => "Восстановление комментария",
            CommentMutationKind.Like => "Лайк комментария",
            CommentMutationKind.Unlike => "Снятие лайка",
            _ => "Комментарий",
        };

        var cts = new CancellationTokenSource();
        var action = _actionHolder.Register($"{label}: «{media.Title}»", "Запущена", 1, cts);

        var patchedInPlace = false;

        try
        {
            switch (request.Kind)
            {
                case CommentMutationKind.Create:
                    {
                        var record = await Task.Run(() =>
                                _commentsService.CreateCommentAsync(source, link, request.ParentExternalCommentId, request.Text, request.AuthorId, cts.Token),
                            cts.Token);

                        var groupKey = CommentsBrowserView.BuildGroupKey(media, source.Id, link.ExternalId);
                        var parentCompositeId = string.IsNullOrEmpty(request.ParentExternalCommentId)
                            ? null
                            : $"{source.Id}|{link.ExternalId}|{request.ParentExternalCommentId}";

                        patchedInPlace = uiBrowserView.TryApplyCreate(_orcestrator, record, groupKey, parentCompositeId);
                        break;
                    }

                case CommentMutationKind.Edit:
                    {
                        var commentId = request.ExternalCommentId
                                        ?? throw new InvalidOperationException("Edit без commentId");

                        var record = await Task.Run(() =>
                                _commentsService.EditCommentAsync(source, link, commentId, request.Text, cts.Token),
                            cts.Token);

                        patchedInPlace = uiBrowserView.TryApplyEdit(record.Id, record.Text);
                        break;
                    }

                case CommentMutationKind.Delete:
                    {
                        var commentId = request.ExternalCommentId
                                        ?? throw new InvalidOperationException("Delete без commentId");

                        await Task.Run(() =>
                                _commentsService.DeleteCommentAsync(source, link, commentId, cts.Token),
                            cts.Token);

                        var recordId = $"{source.Id}|{link.ExternalId}|{commentId}";
                        patchedInPlace = uiBrowserView.TryApplyDeleted(recordId, true);
                        break;
                    }

                case CommentMutationKind.Restore:
                    {
                        var commentId = request.ExternalCommentId
                                        ?? throw new InvalidOperationException("Restore без commentId");

                        await Task.Run(() =>
                                _commentsService.RestoreCommentAsync(source, link, commentId, cts.Token),
                            cts.Token);

                        var recordId = $"{source.Id}|{link.ExternalId}|{commentId}";
                        patchedInPlace = uiBrowserView.TryApplyDeleted(recordId, false);
                        break;
                    }

                case CommentMutationKind.Like:
                case CommentMutationKind.Unlike:
                    {
                        var liked = request.Kind == CommentMutationKind.Like;
                        var commentId = request.ExternalCommentId
                                        ?? throw new InvalidOperationException($"{request.Kind} без commentId");

                        var newCount = await Task.Run(() =>
                                _commentsService.LikeCommentAsync(source, link, commentId, liked, cts.Token),
                            cts.Token);

                        var recordId = $"{source.Id}|{link.ExternalId}|{commentId}";
                        patchedInPlace = uiBrowserView.TryApplyLikeUpdate(recordId, liked, newCount);
                        break;
                    }
            }

            action.Status = $"{source.TitleFull}: ок";
        }
        finally
        {
            action.ProgressPlus();
            action.Finish();
        }

        if (!patchedInPlace)
        {
            ApplyFilters();
        }
    }

    private IReadOnlyList<CommentAuthorView> ResolveCommentAuthors(string sourceId, string externalMediaId)
    {
        var now = DateTime.UtcNow;
        var cacheKey = (sourceId, externalMediaId);

        if (_authorsCache.TryGetValue(cacheKey, out var cached) && now - cached.FetchedAt < AuthorsCacheTtl)
        {
            return cached.Authors;
        }

        TriggerAuthorsLoad(sourceId, externalMediaId);
        return [];
    }

    private void TriggerAuthorsLoad(string sourceId, string externalMediaId)
    {
        if (_orcestrator == null || _commentsService == null)
        {
            return;
        }

        var cacheKey = (sourceId, externalMediaId);
        var orcestrator = _orcestrator;
        var commentsService = _commentsService;

        _ = Task.Run(async () =>
        {
            try
            {
                var source = orcestrator.GetSources().FirstOrDefault(s => s.Id == sourceId);
                if (source?.Type is not ISupportsCommentAuthors)
                {
                    _authorsCache[cacheKey] = (DateTime.UtcNow, []);
                    return;
                }

                var link = orcestrator.GetMedias()
                    .SelectMany(m => m.Sources)
                    .FirstOrDefault(l => l.SourceId == sourceId && l.ExternalId == externalMediaId);

                if (link == null)
                {
                    return;
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var authors = await commentsService.GetAvailableAuthorsAsync(source, link, cts.Token);
                var view = authors
                    .Select(a => new CommentAuthorView(a.Id, a.Name, a.AvatarUrl, a.IsDefault))
                    .ToList();

                _authorsCache[cacheKey] = (DateTime.UtcNow, view);
                NotifyAuthorsReady(sourceId, externalMediaId, view);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("Превышено время ожидания списка авторов для {Source}/{Media}", sourceId, externalMediaId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Не удалось получить список авторов для {Source}/{Media}", sourceId, externalMediaId);
            }
        });
    }

    private void NotifyAuthorsReady(string sourceId, string externalMediaId, IReadOnlyList<CommentAuthorView> view)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => uiBrowserView.NotifyAuthors(sourceId, externalMediaId, view));
            }
            else
            {
                uiBrowserView.NotifyAuthors(sourceId, externalMediaId, view);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void WarmAuthors(IReadOnlyList<CommentRecord> records)
    {
        if (_orcestrator == null || _commentsService == null)
        {
            return;
        }

        var orcestrator = _orcestrator;
        var commentsService = _commentsService;

        _ = Task.Run(async () =>
        {
            var sourcesById = orcestrator.GetSources().ToDictionary(s => s.Id);
            var now = DateTime.UtcNow;

            var linkByKey = orcestrator.GetMedias()
                .SelectMany(m => m.Sources)
                .Where(l => !string.IsNullOrEmpty(l.SourceId) && !string.IsNullOrEmpty(l.ExternalId))
                .GroupBy(l => (l.SourceId, l.ExternalId))
                .ToDictionary(g => g.Key, g => g.First());

            const int maxWarmPerCall = 20;

            var keys = records
                .Select(r => (r.SourceId, r.ExternalMediaId))
                .Distinct()
                .Where(key =>
                {
                    if (!sourcesById.TryGetValue(key.SourceId, out var src) || src.Type is not ISupportsCommentAuthors)
                    {
                        return false;
                    }

                    return !_authorsCache.TryGetValue(key, out var cached)
                           || now - cached.FetchedAt >= AuthorsCacheTtl;
                })
                .Take(maxWarmPerCall)
                .ToList();

            if (keys.Count == 0)
            {
                return;
            }

            foreach (var key in keys)
            {
                try
                {
                    if (!sourcesById.TryGetValue(key.SourceId, out var source))
                    {
                        continue;
                    }

                    if (!linkByKey.TryGetValue((key.SourceId, key.ExternalMediaId), out var link))
                    {
                        continue;
                    }

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var authors = await commentsService.GetAvailableAuthorsAsync(source, link, cts.Token);
                    var view = authors
                        .Select(a => new CommentAuthorView(a.Id, a.Name, a.AvatarUrl, a.IsDefault))
                        .ToList();

                    _authorsCache[key] = (DateTime.UtcNow, view);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Прогрев авторов не удался для {Source}/{Media}", key.SourceId, key.ExternalMediaId);
                }
            }
        });
    }

    private async Task FetchForExternalAsync(string sourceId, string externalId)
    {
        if (_orcestrator == null || _commentsService == null || _actionHolder == null)
        {
            return;
        }

        var source = _orcestrator.GetSources().FirstOrDefault(s => s.Id == sourceId);
        if (source == null || source.Type is not ISupportsComments)
        {
            return;
        }

        var media = _orcestrator.GetMedias()
            .FirstOrDefault(m => m.Sources.Any(l => l.SourceId == sourceId && l.ExternalId == externalId));

        var link = media?.Sources.FirstOrDefault(l => l.SourceId == sourceId && l.ExternalId == externalId);
        if (media == null || link == null)
        {
            MessageBox.Show("Медиа не найдено локально — нечего обновлять.",
                "Загрузка комментариев",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        var cts = new CancellationTokenSource();
        var action = _actionHolder.Register($"Комментарии: «{media.Title}»", "Запущена", 1, cts);

        uiFetchProgressBar.Style = ProgressBarStyle.Marquee;
        uiFetchProgressBar.Visible = true;
        uiStatusLabel.Text = $"Загрузка комментариев: «{media.Title}»...";

        IProgress<string> progress = new Progress<string>(text =>
        {
            uiStatusLabel.Text = text;
            action.Status = text;
        });

        try
        {
            await Task.Run(async () =>
            {
                var count = await _commentsService.RefreshAsync(source, media, link, progress, cts.Token);
                action.Status = $"{source.TitleFull}: {count}";
            }, cts.Token);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            uiStatusLabel.Text = "Загрузка отменена";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Не удалось загрузить комментарии для «{Title}»", media.Title);
            uiStatusLabel.Text = "Ошибка загрузки";
        }
        finally
        {
            action.ProgressPlus();
            action.Finish();
            uiFetchProgressBar.Visible = false;
            uiFetchProgressBar.Style = ProgressBarStyle.Blocks;
        }

        if (!TryApplyFetchedGroup(media, link))
        {
            ApplyFilters();
        }
    }

    private bool TryApplyFetchedGroup(Media media, MediaSourceLink link)
    {
        if (_orcestrator == null || _commentsService == null)
        {
            return false;
        }

        try
        {
            var records = _commentsService.GetByMedia(link.SourceId, link.ExternalId);
            BackfillOrphanParents(records, _commentsService, CancellationToken.None);

            var groupKey = CommentsBrowserView.BuildGroupKey(media, link.SourceId, link.ExternalId);
            var options = new CommentsRenderOptions
            {
                Search = uiSearchTextBox.Text.Trim(),
                CommentSort = (uiCommentSortComboBox.SelectedItem as SortItem<CommentSort>)?.Value ?? CommentSort.ByDate,
                InvertCommentSort = _invertCommentSort,
                MediaSort = (uiMediaSortComboBox.SelectedItem as SortItem<MediaSort>)?.Value ?? MediaSort.ByTitle,
                InvertMediaSort = _invertMediaSort,
                MediaSearch = uiMediaSearchTextBox.Text.Trim(),
            };

            return uiBrowserView.TryApplyFetched(_orcestrator, records, groupKey, options);
        }
        catch
        {
            return false;
        }
    }

    private async Task FetchAllForSourceAsync(Source source)
    {
        if (_orcestrator == null || _commentsService == null || _actionHolder == null)
        {
            return;
        }

        if (source.Type is not ISupportsComments)
        {
            MessageBox.Show($"Источник «{source.TitleFull}» не поддерживает комментарии.",
                "Невозможно загрузить",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        var sinceDays = (int)uiFetchSinceDaysNumeric.Value > 0 ? (int)uiFetchSinceDaysNumeric.Value : (int?)null;
        var since = sinceDays != null ? DateTime.UtcNow.AddDays(-sinceDays.Value) : (DateTime?)null;

        var takeRecent = (int)uiFetchOnlyRecentNumeric.Value > 0 ? (int)uiFetchOnlyRecentNumeric.Value : (int?)null;

        var staleDays = (int)uiFetchStaleNumeric.Value > 0 ? (int)uiFetchStaleNumeric.Value : (int?)null;
        var staleThreshold = staleDays != null ? DateTime.UtcNow.AddDays(-staleDays.Value) : (DateTime?)null;

        var allForSource = _orcestrator.GetMedias()
            .SelectMany(media => media.Sources
                .Where(link => link.SourceId == source.Id && !string.IsNullOrEmpty(link.ExternalId))
                .Select(link => (Media: media, Link: link)))
            .OrderByDescending(media => media.Link.SortNumber)
            .ToList();

        IEnumerable<(Media Media, MediaSourceLink Link)> filtered = allForSource;

        if (since != null)
        {
            filtered = filtered.Where(t => TryGetCreationDate(t.Media) is { } d && d >= since.Value);
        }

        if (staleThreshold != null)
        {
            filtered = filtered.Where(t => t.Link.CommentsFetchedAt == null || t.Link.CommentsFetchedAt <= staleThreshold.Value);
        }

        if (takeRecent != null)
        {
            filtered = filtered.OrderByDescending(t => t.Link.SortNumber).Take(takeRecent.Value);
        }

        var targets = filtered.ToList();
        var hasFilters = since != null || takeRecent != null || staleThreshold != null;
        var filterSummary = BuildFilterSummary(sinceDays, takeRecent, staleDays);

        if (targets.Count == 0)
        {
            MessageBox.Show(hasFilters
                    ? $"В источнике «{source.TitleFull}» нет медиа, попадающих под фильтры ({filterSummary})."
                    : $"В источнике «{source.TitleFull}» нет медиа для загрузки.",
                "Нечего загружать",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        var confirmMessage = hasFilters
            ? $"Загрузить комментарии для {targets.Count} из {allForSource.Count} медиа источника «{source.TitleFull}»?{Environment.NewLine}Фильтр: {filterSummary}."
            : $"Загрузить комментарии для {targets.Count} медиа источника «{source.TitleFull}»?";

        var confirm = MessageBox.Show(confirmMessage,
            "Подтверждение",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.OK)
        {
            return;
        }

        uiForceFetchAllButton.Enabled = false;
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var action = _actionHolder.Register($"Загрузка комментариев из «{source.TitleFull}»",
            "Запущена",
            targets.Count,
            cts);

        uiFetchProgressBar.Style = ProgressBarStyle.Blocks;
        uiFetchProgressBar.Maximum = targets.Count;
        uiFetchProgressBar.Value = 0;
        uiFetchProgressBar.Visible = true;
        uiFetchCounterLabel.Text = $"0 / {targets.Count}";
        uiFetchCounterLabel.Visible = true;
        uiStatusLabel.Text = $"Загрузка комментариев из «{source.TitleFull}»...";

        var ok = 0;
        var failed = 0;
        var processed = 0;

        IProgress<FetchProgress> reporter = new Progress<FetchProgress>(p =>
        {
            uiFetchProgressBar.Value = p.Processed;
            uiStatusLabel.Text = p.StatusText;
            uiFetchCounterLabel.Text = p.CounterText;
        });

        var counterLock = new object();

        try
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 3,
                CancellationToken = token,
            };

            await Parallel.ForEachAsync(targets, parallelOptions, async (item, ct) =>
            {
                var (media, link) = item;

                int currentIndex;
                string startStatus;
                lock (counterLock)
                {
                    currentIndex = processed + 1;
                    startStatus = $"[{currentIndex}/{targets.Count}] Загрузка: «{media.Title}»";
                    var startCounter = failed > 0
                        ? $"✓ {ok}  ✗ {failed}  /  {targets.Count}"
                        : $"✓ {ok}  /  {targets.Count}";

                    reporter.Report(new(processed, startStatus, startCounter));
                }

                action.Status = $"{media.Title}: загрузка...";

                try
                {
                    var count = await _commentsService.RefreshAsync(source, media, link, null, ct);

                    lock (counterLock)
                    {
                        ok++;
                    }

                    action.Status = $"{media.Title}: {count}";
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lock (counterLock)
                    {
                        failed++;
                    }

                    _logger?.LogError(ex, "Не удалось загрузить комментарии для «{Title}» ({SourceId}/{ExternalId})",
                        media.Title, link.SourceId, link.ExternalId);
                }

                action.ProgressPlus();

                lock (counterLock)
                {
                    processed++;
                    var counter = failed > 0
                        ? $"✓ {ok}  ✗ {failed}  /  {targets.Count}"
                        : $"✓ {ok}  /  {targets.Count}";

                    reporter.Report(new(processed, startStatus, counter));
                }
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _logger?.LogInformation("Загрузка комментариев отменена для «{Source}»", source.TitleFull);
        }
        finally
        {
            var summary = $"Источник «{source.TitleFull}»: успешно {ok} из {targets.Count}";
            if (failed > 0)
            {
                summary += $", ошибок {failed}";
            }

            action.Finish(summary);
            uiFetchProgressBar.Visible = false;
            uiFetchCounterLabel.Visible = false;
        }

        ApplyFilters();
        UpdateForceFetchButtonState();
    }

    private sealed record FetchProgress(int Processed, string StatusText, string CounterText);

    private sealed class SourceComboItem(string? sourceId, string label)
    {
        public string? SourceId { get; } = sourceId;
        public string Label { get; } = label;

        public override string ToString()
        {
            return Label;
        }
    }

    private sealed class SortItem<T>(T value, string label) where T : Enum
    {
        public T Value { get; } = value;
        public string Label { get; } = label;

        public override string ToString()
        {
            return Label;
        }
    }
}
