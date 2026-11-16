using CommunityToolkit.Mvvm.ComponentModel;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Merging;
using MediaOrcestrator.Modules;
using System.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class ConflictGroupViewModel : ObservableObject
{
    private readonly List<Source> _allSources;
    private bool _suppress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApplyButtonHint))]
    private bool _isMergeChecked = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleCheck))]
    private bool _isApplied;

    [ObservableProperty]
    private bool _isApplyError;

    [ObservableProperty]
    private string? _resultText;

    public ConflictGroupViewModel(MergeCandidateGroup group, List<Source> allSources)
    {
        Group = group;
        _allSources = allSources;
        Title = group.NormalizedKey;
        var groupKey = Guid.NewGuid().ToString("N");

        var rows = group.Medias
            .Select(m => new ConflictMediaRowViewModel
            {
                GroupKey = groupKey,
                Media = m,
                Title = m.Title,
                SourceBadges = BuildBadges(m),
            })
            .ToList();

        foreach (var row in rows)
        {
            row.PropertyChanged += OnRowChanged;
        }

        Medias = new(rows);

        var target = rows.FirstOrDefault(r => ReferenceEquals(r.Media, group.SuggestedTarget)) ?? rows[0];
        target.IsTarget = true;

        Recompute();
    }

    public MergeCandidateGroup Group { get; }

    public string Title { get; }

    public RangeObservableCollection<ConflictMediaRowViewModel> Medias { get; }

    public RangeObservableCollection<ConflictSourceSlotViewModel> Conflicts { get; } = [];

    public bool IsResolved { get; private set; }

    public string Headline { get; private set; } = string.Empty;

    public string ResultSummary { get; private set; } = string.Empty;

    public string? UnresolvedReason { get; private set; }

    public string ApplyButtonHint => IsMergeChecked ? "Войдёт в объединение" : "Пропущено";

    public bool CanToggleCheck => !IsApplied;

    public IReadOnlyList<ConflictMediaRowViewModel> ActiveRows =>
        Medias.Where(m => !m.IsExcluded).ToList();

    public ConflictMediaRowViewModel? TargetRow =>
        Medias.FirstOrDefault(m => m is { IsTarget: true, IsExcluded: false });

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(ConflictMediaRowViewModel.IsTarget):
                if (sender is ConflictMediaRowViewModel changed && changed.IsTarget)
                {
                    EnforceSingleTarget(changed);
                }

                Recompute();
                break;

            case nameof(ConflictMediaRowViewModel.IsExcluded):
                Recompute();
                break;
        }
    }

    private void EnforceSingleTarget(ConflictMediaRowViewModel chosen)
    {
        _suppress = true;
        try
        {
            foreach (var row in Medias)
            {
                row.IsTarget = ReferenceEquals(row, chosen);
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppress || e.PropertyName != nameof(ConflictSourceSlotViewModel.SelectedChoice))
        {
            return;
        }

        RefreshState();
    }

    private void Recompute()
    {
        var active = ActiveRows;

        if (TargetRow is null && active.Count > 0)
        {
            var defaultTarget = MediaMergeService.ChooseTarget(active.Select(r => r.Media).ToList());
            var row = active.First(r => ReferenceEquals(r.Media, defaultTarget));
            _suppress = true;
            try
            {
                row.IsTarget = true;
            }
            finally
            {
                _suppress = false;
            }
        }

        RebuildConflicts(active);
        RefreshState();
    }

    private void RebuildConflicts(IReadOnlyList<ConflictMediaRowViewModel> active)
    {
        var previousChoice = Conflicts.ToDictionary(c => c.SourceId, c => c.SelectedChoice?.ExternalId, StringComparer.Ordinal);

        foreach (var slot in Conflicts)
        {
            slot.PropertyChanged -= OnSlotChanged;
        }

        var slots = new List<ConflictSourceSlotViewModel>();

        var bySource = active
            .SelectMany(row => (row.Media.Sources ?? []).Select(link => (row, link)))
            .GroupBy(x => x.link.SourceId, StringComparer.Ordinal);

        foreach (var sourceGroup in bySource)
        {
            var links = sourceGroup.ToList();

            if (links.Count < 2)
            {
                continue;
            }

            var choices = links
                .Select(x => new ConflictLinkChoiceViewModel
                {
                    MediaId = x.row.Media.Id,
                    ExternalId = x.link.ExternalId,
                    Status = x.link.Status,
                    MediaTitle = x.row.Title,
                    Label = $"{x.row.Title} · {StatusText(x.link.Status)} · {x.link.ExternalId}",
                })
                .ToList();

            var slot = new ConflictSourceSlotViewModel
            {
                SourceId = sourceGroup.Key,
                SourceName = SourceName(sourceGroup.Key),
                Choices = choices,
            };

            slot.SelectedChoice = previousChoice.TryGetValue(slot.SourceId, out var prevExt) && prevExt != null
                ? choices.FirstOrDefault(c => c.ExternalId == prevExt) ?? PickLiveliest(choices)
                : PickLiveliest(choices);

            slot.PropertyChanged += OnSlotChanged;
            slots.Add(slot);
        }

        Conflicts.ReplaceAll(slots);
    }

    private void RefreshState()
    {
        var active = ActiveRows;
        var target = TargetRow;
        var conflictsCount = Conflicts.Count;

        Headline = $"{active.Count} медиа · {conflictsCount} конфликт{Plural(conflictsCount)} источника";

        if (active.Count < 2)
        {
            IsResolved = false;
            UnresolvedReason = "Осталось меньше двух медиа – объединять нечего";
        }
        else if (target is null)
        {
            IsResolved = false;
            UnresolvedReason = "Не выбрано целевое медиа";
        }
        else if (Conflicts.Any(c => c.SelectedChoice is null))
        {
            IsResolved = false;
            UnresolvedReason = "Для каждого конфликтного источника выберите ссылку";
        }
        else
        {
            IsResolved = true;
            UnresolvedReason = null;
        }

        ResultSummary = target is null
            ? "–"
            : $"Останется: {target.Title} [{string.Join(", ", ResultingSourceNames(active))}]";

        OnPropertyChanged(nameof(IsResolved));
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(UnresolvedReason));
        OnPropertyChanged(nameof(TargetRow));
        OnPropertyChanged(nameof(ActiveRows));
    }

    private IEnumerable<string> ResultingSourceNames(IReadOnlyList<ConflictMediaRowViewModel> active)
    {
        return active
            .SelectMany(r => r.Media.Sources ?? [])
            .Select(s => s.SourceId)
            .Distinct(StringComparer.Ordinal)
            .Select(SourceName);
    }

    private IReadOnlyList<string> BuildBadges(Media media)
    {
        return (media.Sources ?? [])
            .Select(s => $"{SourceName(s.SourceId)} · {StatusText(s.Status)}")
            .ToList();
    }

    private string SourceName(string sourceId)
    {
        return _allSources.FirstOrDefault(s => s.Id == sourceId)?.Title ?? sourceId;
    }

    private static ConflictLinkChoiceViewModel PickLiveliest(IReadOnlyList<ConflictLinkChoiceViewModel> choices)
    {
        return choices.OrderByDescending(c => StatusRank(c.Status)).First();
    }

    private static string StatusText(string? status)
    {
        if (string.IsNullOrEmpty(status))
        {
            return "–";
        }

        try
        {
            return MediaStatusHelper.GetById(status).Text;
        }
        catch
        {
            return status;
        }
    }

    private static int StatusRank(string? status)
    {
        return status switch
        {
            MediaStatus.Ok => 5,
            MediaStatus.PartialOk => 4,
            MediaStatus.Skipped => 3,
            MediaStatus.None => 2,
            MediaStatus.Error => 1,
            MediaStatus.Missing => 0,
            _ => -1,
        };
    }

    private static string Plural(int count)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;

        if (mod100 is >= 11 and <= 14)
        {
            return "ов";
        }

        return mod10 switch
        {
            1 => "",
            >= 2 and <= 4 => "а",
            _ => "ов",
        };
    }
}

public sealed partial class ConflictMediaRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isTarget;

    // TODO (backlog): «не дубликат» в v1 действует только в рамках текущего разбора – при следующем
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBeTarget))]
    private bool _isExcluded;

    public required string GroupKey { get; init; }

    public required Media Media { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<string> SourceBadges { get; init; }

    public bool CanBeTarget => !IsExcluded;
}

public sealed partial class ConflictSourceSlotViewModel : ObservableObject
{
    [ObservableProperty]
    private ConflictLinkChoiceViewModel? _selectedChoice;

    public required string SourceId { get; init; }

    public required string SourceName { get; init; }

    public required IReadOnlyList<ConflictLinkChoiceViewModel> Choices { get; init; }
}

public sealed class ConflictLinkChoiceViewModel
{
    public required string MediaId { get; init; }

    public required string ExternalId { get; init; }

    public required string Status { get; init; }

    public required string MediaTitle { get; init; }

    public required string Label { get; init; }
}
