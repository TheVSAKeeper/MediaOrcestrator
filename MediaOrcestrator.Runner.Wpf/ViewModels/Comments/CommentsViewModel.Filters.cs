using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using Microsoft.Extensions.Logging;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed partial class CommentsViewModel
{
    [RelayCommand]
    private void ResetFilters()
    {
        SearchText = string.Empty;
        SelectedReplyStatus = ReplyStatusOptions[0];

        foreach (var source in Sources)
        {
            source.IsChecked = false;
        }

        PersistSettings();
        ScheduleApplyFilters();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    partial void OnLimitChanged(int value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.Limit = Math.Clamp(value, 100, 10000);
        PersistSettings();
        ScheduleApplyFilters();
    }

    partial void OnSelectedLayoutChanged(CommentLayoutOption value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.LayoutMode = value.Mode;
        PersistSettings();
        ScheduleApplyFilters();
    }

    partial void OnSelectedReplyStatusChanged(CommentReplyStatusOption value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.ReplyStatus = value.Key;
        PersistSettings();
        ScheduleApplyFilters();
    }

    partial void OnIsFiltersExpandedChanged(bool value) => _settingsStore.SetBool(FiltersExpandedKey, value);

    private void ApplySettingsToProperties()
    {
        SearchText = _settings.Search;
        Limit = Math.Clamp(_settings.Limit <= 0 ? 1000 : _settings.Limit, 100, 10000);
        SelectedLayout = LayoutOptions.FirstOrDefault(o => o.Mode == _settings.LayoutMode) ?? LayoutOptions[0];
        SelectedReplyStatus = ReplyStatusOptions.FirstOrDefault(o => o.Key == _settings.ReplyStatus) ?? ReplyStatusOptions[0];
        CommentsAppearanceResources.Apply(_settings.Appearance);
    }

    private void ReloadSources()
    {
        var supportsComments = _orcestrator.GetSources()
            .Where(s => s.Type is ISupportsComments)
            .OrderBy(s => s.TitleFull, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var checkedIds = new HashSet<string>(_settings.SelectedSourceIds, StringComparer.Ordinal);

        foreach (var existing in Sources)
        {
            existing.PropertyChanged -= OnSourceOptionChanged;
        }

        var newSources = supportsComments
            .Select(src =>
            {
                var option = new CommentSourceFilterOptionViewModel(src.Id, src.TitleFull)
                {
                    IsChecked = checkedIds.Contains(src.Id),
                };
                option.PropertyChanged += OnSourceOptionChanged;
                return option;
            })
            .ToList();

        Sources.ReplaceAll(newSources);
        HasSources = Sources.Count > 0;
        SelectedSourceCount = Sources.Count(s => s.IsChecked);
    }

    private void OnSourceOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommentSourceFilterOptionViewModel.IsChecked))
        {
            return;
        }

        SelectedSourceCount = Sources.Count(s => s.IsChecked);

        if (_suppressPersist)
        {
            return;
        }

        _settings.SelectedSourceIds = Sources.Where(s => s.IsChecked).Select(s => s.SourceId).ToList();
        PersistSettings();
        ScheduleApplyFilters();
    }

    private void OnSourcesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(HasAnySources));

    private void PersistSettings()
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.Search = SearchText;
        _settings.Limit = Limit;
        _settings.LayoutMode = SelectedLayout.Mode;
        _settings.ReplyStatus = SelectedReplyStatus.Key;
        _settings.SelectedSourceIds = Sources.Where(s => s.IsChecked).Select(s => s.SourceId).ToList();

        var snapshot = _settings;
        _ = Task.Run(() => snapshot.Save(_settingsStore));
    }

    private void ScheduleApplyFilters()
    {
        if (_loaded)
        {
            _ = ApplyFiltersAsync();
        }
    }

    private async Task ApplyFiltersAsync()
    {
        var version = Interlocked.Increment(ref _applyVersion);

        _applyCts?.Cancel();
        _applyCts?.Dispose();
        _applyCts = new();
        var ct = _applyCts.Token;

        var selectedSourceIds = Sources.Where(s => s.IsChecked).Select(s => s.SourceId).ToHashSet(StringComparer.Ordinal);
        var sourceIds = selectedSourceIds.Count == 0 ? null : (IReadOnlyCollection<string>)selectedSourceIds;
        var search = (SearchText ?? string.Empty).Trim();
        var limit = Math.Clamp(Limit, 100, 10000);
        var layout = SelectedLayout.Mode;
        var replyStatus = SelectedReplyStatus.Key;
        var callbacks = new CommentsTreeCallbacks(this, ToggleBranchAsync, RefreshGroupAsync, RefreshOneSourceAsync, _sourceFetcher.OpenOneSourceAsync);

        IsLoading = true;
        LoadError = null;

        var totalSw = Stopwatch.StartNew();

        try
        {
            var buildSw = Stopwatch.StartNew();
            var rows = await Task.Run(() => BuildItems(sourceIds, search, limit, layout, replyStatus, callbacks, ct), ct);
            buildSw.Stop();
            var buildMs = buildSw.ElapsedMilliseconds;

            if (version != Volatile.Read(ref _applyVersion))
            {
                return;
            }

            ReplaceItems(rows);

            totalSw.Stop();
            var uiMs = totalSw.ElapsedMilliseconds - buildMs;
            RenderDurationText = $"{totalSw.ElapsedMilliseconds} мс";
            RenderStages =
            [
                new("Подготовка данных", $"{buildMs} мс"),
                new("Применение к UI", $"{uiMs} мс"),
                new("Итого", $"{totalSw.ElapsedMilliseconds} мс"),
                new("Строк", rows.Rows.Count.ToString()),
            ];
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка применения фильтров комментариев");

            if (version == Volatile.Read(ref _applyVersion))
            {
                LoadError = ex.Message;
            }
        }
        finally
        {
            if (version == Volatile.Read(ref _applyVersion))
            {
                IsLoading = false;
            }
        }
    }

    private CommentsBuildResult BuildItems(
        IReadOnlyCollection<string>? sourceIds,
        string search,
        int limit,
        CommentsLayoutMode layout,
        CommentsReplyStatusFilter replyStatus,
        CommentsTreeCallbacks callbacks,
        CancellationToken ct)
    {
        var records = LoadRecords(sourceIds, search, limit + 1, ct);
        var truncated = records.Count > limit;

        if (truncated)
        {
            records = records.Take(limit).ToList();
        }

        BackfillOrphanParents(records, ct);
        return _treeBuilder.Build(records, truncated, search, layout, replyStatus, _expandedBranches, callbacks, ct);
    }

    private List<CommentRecord> LoadRecords(IReadOnlyCollection<string>? sourceIds, string search, int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(search))
        {
            return _commentsService.Query(sourceIds, limit: limit);
        }

        var byText = _commentsService.Query(sourceIds, textContains: search, limit: limit);
        ct.ThrowIfCancellationRequested();

        var sourceFilter = sourceIds is { Count: > 0 } sids
            ? new HashSet<string>(sids, StringComparer.Ordinal)
            : null;

        var titleMatches = _orcestrator.GetMedias()
            .Where(m => !string.IsNullOrEmpty(m.Title) && m.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
            .SelectMany(m => m.Sources)
            .Where(l => !string.IsNullOrEmpty(l.SourceId) && !string.IsNullOrEmpty(l.ExternalId))
            .Where(l => sourceFilter is null || sourceFilter.Contains(l.SourceId))
            .Select(l => (l.SourceId, l.ExternalId))
            .Distinct()
            .ToList();

        if (titleMatches.Count == 0)
        {
            return byText;
        }

        var seen = new HashSet<string>(byText.Select(r => r.Id), StringComparer.Ordinal);
        var combined = new List<CommentRecord>(byText);

        foreach (var (sId, externalId) in titleMatches)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var record in _commentsService.GetByMedia(sId, externalId))
            {
                if (seen.Add(record.Id))
                {
                    combined.Add(record);
                }
            }
        }

        return combined;
    }

    private void BackfillOrphanParents(List<CommentRecord> records, CancellationToken ct)
    {
        if (records.Count == 0)
        {
            return;
        }

        var known = new HashSet<string>(records.Select(r => r.Id), StringComparer.Ordinal);
        var pending = new Queue<string>();

        foreach (var r in records)
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

        var safety = records.Count * 2;

        while (pending.Count > 0 && safety-- > 0)
        {
            ct.ThrowIfCancellationRequested();

            var parent = _commentsService.GetById(pending.Dequeue());

            if (parent is null)
            {
                continue;
            }

            records.Add(parent);

            if (string.IsNullOrEmpty(parent.ParentExternalCommentId))
            {
                continue;
            }

            var gp = $"{parent.SourceId}|{parent.ExternalMediaId}|{parent.ParentExternalCommentId}";

            if (known.Add(gp))
            {
                pending.Enqueue(gp);
            }
        }
    }

    private void ReplaceItems(CommentsBuildResult result)
    {
        lock (_itemsLock)
        {
            Items.ReplaceAll(result.Rows);
        }

        TotalCountText = result.TotalCount switch
        {
            0 => "Комментариев: 0",
            _ when result.Truncated => $"Комментариев: ≥{result.TotalCount} (увеличьте лимит)",
            _ => $"Комментариев: {result.TotalCount}",
        };

        TruncatedHint = result.Truncated ? "Увеличьте лимит, чтобы загрузить больше." : null;

        (EmptyIcon, EmptyHeading, EmptyMessage) = result.Rows.Count > 0
            ? (PackIconLucideKind.MessageSquare, "Комментариев нет", (string?)null)
            : !HasSources
                ? (PackIconLucideKind.SlidersHorizontal, "Источники не настроены", "Сначала добавьте источники в разделе «Источники и связи».")
                : SelectedSourceCount == 0
                    ? (PackIconLucideKind.SlidersHorizontal, "Источники не выбраны", "Выберите источники в фильтре и нажмите «Загрузить», чтобы получить комментарии.")
                    : (PackIconLucideKind.MessageSquare, "Комментариев нет", "Под текущие фильтры ничего не подошло. Сбросьте поиск или статус ответа.");
    }

    private Task ToggleBranchAsync(BranchToggleViewModel toggle)
    {
        if (!_expandedBranches.Add(toggle.RootId))
        {
            _expandedBranches.Remove(toggle.RootId);
        }

        ScheduleApplyFilters();
        return Task.CompletedTask;
    }

    private async Task RefreshGroupAsync(MediaGroupHeaderViewModel header)
    {
        var infos = header.Sources.Where(s => s.HasFetchSupport).ToList();

        if (infos.Count == 0)
        {
            return;
        }

        foreach (var info in infos)
        {
            await _sourceFetcher.RefreshSourceMediaAsync(info.SourceId, info.ExternalMediaId);
        }

        ScheduleApplyFilters();
    }

    private async Task RefreshOneSourceAsync(MediaGroupSourceActionViewModel action)
    {
        await _sourceFetcher.RefreshSourceMediaAsync(action.SourceId, action.ExternalMediaId);
        ScheduleApplyFilters();
    }
}
