using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Merging;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner;

public partial class MergeAssistantForm : Form
{
    private readonly Orcestrator? _orcestrator;
    private readonly MediaMergeService? _mergeService;
    private readonly ILogger<MergeAssistantForm>? _logger;
    private readonly List<MergeCandidateGroup> _groups = [];

    public MergeAssistantForm()
    {
        InitializeComponent();
    }

    public MergeAssistantForm(
        Orcestrator orcestrator,
        MediaMergeService mergeService,
        ILogger<MergeAssistantForm> logger)
        : this()
    {
        _orcestrator = orcestrator;
        _mergeService = mergeService;
        _logger = logger;
    }

    public int AppliedCount { get; private set; }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (_orcestrator == null)
        {
            return;
        }

        PopulateSourceScope();
        UpdateStatus();
    }

    private void uiFindCandidatesButton_Click(object? sender, EventArgs e)
    {
        if (_orcestrator == null!)
        {
            return;
        }

        _groups.Clear();
        uiGroupsGrid.Rows.Clear();

        var scope = uiSourceScopeList.CheckedItems
            .OfType<SourceScopeItem>()
            .Select(i => i.Source.Id)
            .ToHashSet();

        if (scope.Count == 0)
        {
            MessageBox.Show("Отметьте хотя бы один источник",
                "Пустая область поиска",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        var allMedia = _orcestrator.GetMedias();
        var allSources = _orcestrator.GetSources();
        var groups = MergeCandidateFinder.FindGroups(allMedia, scope);

        foreach (var group in groups)
        {
            _groups.Add(group);

            var mergingText = string.Join(" ⋅ ",
                group.Medias
                    .Where(m => !ReferenceEquals(m, group.SuggestedTarget))
                    .Select(m => FormatMedia(m, allSources)));

            var totalSources = group.Medias
                .SelectMany(m => m.Sources ?? [])
                .Select(s => s.SourceId)
                .Distinct()
                .Count();

            uiGroupsGrid.Rows.Add(true,
                group.NormalizedKey,
                FormatMedia(group.SuggestedTarget, allSources),
                mergingText,
                totalSources);
        }

        UpdateStatus();

        if (groups.Count == 0)
        {
            MessageBox.Show("Кандидатов для объединения не найдено",
                "Пусто",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void uiSelectAllButton_Click(object? sender, EventArgs e)
    {
        SetAllChecked(true);
    }

    private void uiSelectNoneButton_Click(object? sender, EventArgs e)
    {
        SetAllChecked(false);
    }

    private void uiGroupsGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 0)
        {
            return;
        }

        UpdateStatus();
    }

    private void uiGroupsGrid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (uiGroupsGrid.CurrentCell is DataGridViewCheckBoxCell)
        {
            uiGroupsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    // TODO: Сделать async с прогрессом в uiStatusLabel - сейчас на батче 480 групп UI виснет на время LiteDB-транзакций
    private void uiApplyButton_Click(object? sender, EventArgs e)
    {
        if (_mergeService == null || _logger == null)
        {
            return;
        }

        var indices = GetCheckedIndices();

        if (indices.Count == 0)
        {
            return;
        }

        var confirmation = MessageBox.Show($"Объединить {indices.Count} групп(ы)? Это действие нельзя будет отменить.",
            "Подтверждение",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        var success = 0;
        var failures = new List<string>();

        uiApplyButton.Enabled = false;
        uiFindCandidatesButton.Enabled = false;
        UseWaitCursor = true;

        try
        {
            foreach (var index in indices)
            {
                var group = _groups[index];

                try
                {
                    var preview = _mergeService.BuildPreview(group.Medias, group.SuggestedTarget);
                    _mergeService.Apply(preview);
                    success++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Не удалось объединить группу '{Key}'", group.NormalizedKey);
                    failures.Add($"{group.NormalizedKey}: {ex.Message}");
                }
            }
        }
        finally
        {
            UseWaitCursor = false;
            uiFindCandidatesButton.Enabled = true;
        }

        AppliedCount += success;

        var failureSummary = failures.Count == 0
            ? string.Empty
            : $"""


               Ошибки:
               {string.Join('\n', failures.Take(10))}
               """;

        MessageBox.Show($"Объединено групп: {success}. С ошибками: {failures.Count}.{failureSummary}",
            "Готово",
            MessageBoxButtons.OK,
            failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        DialogResult = DialogResult.OK;
        Close();
    }

    private static string FormatMedia(Media media, List<Source> allSources)
    {
        var sourceNames = (media.Sources ?? [])
            .Select(s => allSources.FirstOrDefault(x => x.Id == s.SourceId)?.Title ?? s.SourceId);

        return $"{media.Title} [{string.Join(", ", sourceNames)}]";
    }

    private void PopulateSourceScope()
    {
        if (_orcestrator == null)
        {
            return;
        }

        uiSourceScopeList.Items.Clear();
        var sources = _orcestrator.GetSources().Where(s => !s.IsDisable).OrderBy(s => s.Title).ToList();

        foreach (var source in sources)
        {
            uiSourceScopeList.Items.Add(new SourceScopeItem(source), true);
        }
    }

    private void SetAllChecked(bool value)
    {
        foreach (DataGridViewRow row in uiGroupsGrid.Rows)
        {
            row.Cells[0].Value = value;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var totalGroups = _groups.Count;
        var checkedGroups = GetCheckedIndices().Count;
        var toRemove = GetCheckedIndices().Sum(i => _groups[i].Medias.Count - 1);
        uiStatusLabel.Text = $"Групп: {totalGroups}. Отмечено: {checkedGroups}. Будет удалено медиа: {toRemove}.";
        uiApplyButton.Enabled = checkedGroups > 0;
    }

    private List<int> GetCheckedIndices()
    {
        var result = new List<int>();

        for (var i = 0; i < uiGroupsGrid.Rows.Count && i < _groups.Count; i++)
        {
            var cell = uiGroupsGrid.Rows[i].Cells[0];

            if (cell.Value is true)
            {
                result.Add(i);
            }
        }

        return result;
    }

    private sealed class SourceScopeItem(Source source)
    {
        public Source Source { get; } = source;

        public override string ToString()
        {
            return Source.TitleFull;
        }
    }
}
