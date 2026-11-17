using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Services;
using System.Diagnostics;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class TransfersViewModel
{
    private TransferFilterBreakdown ApplyPlanInternal(List<SyncIntent> rootIntents, int totalSteps)
    {
        _allRootIntents.Clear();
        _allRootIntents.AddRange(rootIntents);

        PlanRoots = rootIntents.Count;
        PlanSteps = totalSteps;
        IsPlanLoaded = true;
        IsPlanStale = false;

        return ApplyFilter();
    }

    private void ClearPlanOnError()
    {
        _allRootIntents.Clear();

        foreach (var group in Groups)
        {
            DetachGroup(group);
        }

        Groups.ReplaceAll([]);
        RecomputeCounts();
        PlanRoots = 0;
        PlanSteps = 0;
        IsPlanLoaded = false;
        IsPlanStale = false;
    }

    private TransferFilterBreakdown ApplyFilter()
    {
        var groupingSw = Stopwatch.StartNew();

        foreach (var group in Groups)
        {
            DetachGroup(group);
        }

        if (_allRootIntents.Count == 0)
        {
            Groups.ReplaceAll([]);
            RecomputeCounts();
            groupingSw.Stop();
            return new(groupingSw.ElapsedMilliseconds, 0, 0);
        }

        var search = SearchText?.Trim() ?? string.Empty;
        var sourceFilter = SourceFilters
            .Where(f => f.IsChecked)
            .Select(f => f.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationFilter = RelationFilters
            .Where(f => f.IsChecked)
            .Select(f => f.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var groupedByMedia = _allRootIntents
            .GroupBy(i => i.Media.Id)
            .OrderBy(g => g.Min(i => i.Sort))
            .ThenBy(g => g.First().Media.Title, NaturalStringComparer.Instance)
            .ToList();
        groupingSw.Stop();

        var nodesSw = Stopwatch.StartNew();
        var newGroups = new List<TransferMediaGroupViewModel>();

        foreach (var bucket in groupedByMedia)
        {
            var media = bucket.First().Media;

            if (search.Length > 0 && media.Title.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (sourceFilter.Count > 0 && !media.Sources.Any(s => sourceFilter.Contains(s.SourceId)))
            {
                continue;
            }

            var group = new TransferMediaGroupViewModel(this, media);
            var rootNodes = new List<TransferIntentNodeViewModel>();

            foreach (var rootIntent in bucket.OrderBy(i => i.From.TitleFull, NaturalStringComparer.Instance).ThenBy(i => i.Sort))
            {
                if (relationFilter.Count > 0 && !relationFilter.Contains(RelationKey(rootIntent.Relation)))
                {
                    continue;
                }

                rootNodes.Add(BuildNode(rootIntent, parent: null, relationFilter));
            }

            if (rootNodes.Count == 0)
            {
                continue;
            }

            group.Children.AddRange(rootNodes);
            group.Recompute();
            newGroups.Add(group);
        }

        nodesSw.Stop();

        var commitSw = Stopwatch.StartNew();

        foreach (var group in newGroups)
        {
            AttachGroup(group);
        }

        Groups.ReplaceAll(newGroups);
        RecomputeCounts();
        commitSw.Stop();

        return new(groupingSw.ElapsedMilliseconds, nodesSw.ElapsedMilliseconds, commitSw.ElapsedMilliseconds);
    }

    private static string RelationKey(SourceSyncRelation relation) => $"{relation.FromId}|{relation.ToId}";

    private void PopulateFilters(List<Source> sources, List<SourceSyncRelation> relations)
    {
        _suppressFilterApply = true;

        var prevSources = SourceFilters.Where(f => f.IsChecked).Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prevRelations = RelationFilters.Where(f => f.IsChecked).Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        SourceFilters.ReplaceAll(sources
            .OrderBy(s => s.TitleFull, NaturalStringComparer.Instance)
            .Select(s => new FilterOptionViewModel(s.Id, s.TitleFull, OnFilterToggled)
            {
                IsChecked = prevSources.Contains(s.Id),
            }));

        RelationFilters.ReplaceAll(relations
            .Where(r => r.From is not null && r.To is not null)
            .Select(r =>
            {
                var id = RelationKey(r);
                return new FilterOptionViewModel(id, $"{r.From.TitleFull} → {r.To.TitleFull}", OnFilterToggled)
                {
                    IsChecked = prevRelations.Contains(id),
                };
            }));

        _suppressFilterApply = false;
    }

    private void OnFilterToggled(FilterOptionViewModel _)
    {
        if (_suppressFilterApply)
        {
            return;
        }

        ApplyFilterAndPublish();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OnSearchTimerTick()
    {
        _searchTimer.Stop();
        ApplyFilterAndPublish();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        _searchTimer.Stop();
        ApplyFilterAndPublish();
    }

    [RelayCommand]
    private void ResetFilters()
    {
        _suppressFilterApply = true;

        foreach (var f in SourceFilters)
        {
            f.IsChecked = false;
        }

        foreach (var f in RelationFilters)
        {
            f.IsChecked = false;
        }

        SearchText = string.Empty;
        _searchTimer.Stop();
        _suppressFilterApply = false;

        ApplyFilterAndPublish();
        IsMoreMenuOpen = false;
    }

    private void ApplyFilterAndPublish()
    {
        var b = ApplyFilter();
        var vmMs = b.GroupingMs + b.NodesMs + b.CommitMs;
        PublishRenderMetric(
        [
            new("Группировка медиа", FormatMs(b.GroupingMs)),
            new("Построение узлов дерева", FormatMs(b.NodesMs)),
            new("Подключение и пересчёт", FormatMs(b.CommitMs)),
        ], vmMs);
    }

    private TransferIntentNodeViewModel BuildNode(SyncIntent intent, TransferIntentNodeViewModel? parent, HashSet<string>? relationFilter = null)
    {
        var node = new TransferIntentNodeViewModel(this, intent, parent);
        node.PropertyChanged += OnNodePropertyChanged;

        var childNodes = new List<TransferIntentNodeViewModel>(intent.NextIntents.Count);

        foreach (var child in intent.NextIntents)
        {
            if (relationFilter is { Count: > 0 } && !relationFilter.Contains(RelationKey(child.Relation)))
            {
                continue;
            }

            childNodes.Add(BuildNode(child, node, relationFilter));
        }

        if (childNodes.Count > 0)
        {
            node.Children.AddRange(childNodes);
        }

        return node;
    }

    internal void SetSearchImmediateForAutomation(string searchText)
    {
        SearchText = searchText;
        _searchTimer.Stop();
        ApplyFilter();
    }

    internal void SetErrorForAutomation(bool error)
    {
        IsRunning = false;
        RunStepsTotal = error ? 10 : 0;
        RunStepsCompleted = error ? 6 : 0;
        RunStepsFailed = error ? 4 : 0;
        StatusText = error ? "Ошибка переноса: квота YouTube исчерпана" : "Готов";

        var firstNode = Groups.SelectMany(g => g.Children).FirstOrDefault();

        if (firstNode is null)
        {
            return;
        }

        if (error)
        {
            firstNode.StatusKind = TransferStatusKind.Error;
            firstNode.StatusTail = "(Ошибка: квота API исчерпана)";
            firstNode.ErrorTooltip = "Квота YouTube API исчерпана";
        }
        else
        {
            firstNode.ResetRunState();
        }
    }

    private sealed record TransferFilterBreakdown(long GroupingMs, long NodesMs, long CommitMs);
}
