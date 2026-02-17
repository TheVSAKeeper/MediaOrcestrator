using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner;

public partial class SyncTreeControl : UserControl
{
    private Orcestrator? _orcestrator;
    private ILogger<SyncTreeControl>? _logger;
    private List<SyncIntent>? _rootIntents;

    public SyncTreeControl()
    {
        InitializeComponent();
        uiTreeView.CheckBoxes = true;
    }

    public void Initialize(List<SyncIntent> rootIntents, Orcestrator orcestrator, ILogger<SyncTreeControl> logger)
    {
        _orcestrator = orcestrator;
        _logger = logger;
        _rootIntents = rootIntents;
        PopulateTree();
    }

    private async void uiExecuteButton_Click(object? sender, EventArgs e)
    {
        if (_rootIntents == null || _orcestrator == null || _logger == null)
        {
            return;
        }

        UpdateIntentsFromTree(uiTreeView.Nodes);

        var selectedRootIntents = _rootIntents.Where(i => i.IsSelected).ToList();
        if (selectedRootIntents.Count == 0)
        {
            MessageBox.Show("Ничего не выбрано.");
            return;
        }

        uiExecuteButton.Enabled = false;
        uiTreeView.Enabled = false;

        try
        {
            foreach (var intent in selectedRootIntents)
            {
                try
                {
                    await ExecuteIntent(intent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при выполнении синхронизации для {Intent}", intent);
                }
            }

            MessageBox.Show("Процесс синхронизации завершен. Проверьте логи на наличие ошибок.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Критическая ошибка при выполнении синхронизации.");
            MessageBox.Show($"Критическая ошибка: {ex.Message}");
        }
        finally
        {
            uiExecuteButton.Enabled = true;
            uiTreeView.Enabled = true;
        }
    }

    private static TreeNode CreateNode(SyncIntent intent)
    {
        var node = new TreeNode(intent.ToString())
        {
            Tag = intent,
            Checked = intent.IsSelected,
        };

        foreach (var nextIntent in intent.NextIntents)
        {
            node.Nodes.Add(CreateNode(nextIntent));
        }

        return node;
    }

    private static void UpdateIntentsFromTree(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is not SyncIntent intent)
            {
                continue;
            }

            intent.IsSelected = node.Checked;
            UpdateIntentsFromTree(node.Nodes);
        }
    }

    private void PopulateTree()
    {
        uiTreeView.Nodes.Clear();
        if (_rootIntents == null)
        {
            return;
        }

        foreach (var node in _rootIntents.Select(CreateNode))
        {
            uiTreeView.Nodes.Add(node);
        }

        uiTreeView.ExpandAll();
    }

    private async Task ExecuteIntent(SyncIntent intent)
    {
        if (_orcestrator == null || _logger == null)
        {
            return;
        }

        if (!intent.IsSelected)
        {
            return;
        }

        _logger.LogInformation("Выполнение: {Intent}", intent);

        var fromMediaSource = intent.Media.Sources.FirstOrDefault(x => x.SourceId == intent.From.Id);
        if (fromMediaSource == null)
        {
            _logger.LogWarning("MediaSourceLink не найден для {SourceId} у медиа {MediaId}", intent.From.Id, intent.Media.Id);
            return;
        }

        await _orcestrator.TransferByRelation(intent.Media, intent.Relation, fromMediaSource.ExternalId);

        foreach (var nextIntent in intent.NextIntents)
        {
            await ExecuteIntent(nextIntent);
        }
    }
}
