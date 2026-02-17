using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner;

public partial class SyncTreeControl : UserControl
{
    private readonly SyncPlanGenerator _generator;
    private readonly SyncExecutor _executor;
    private readonly ILogger<SyncTreeControl> _logger;
    private SyncPlan? _currentPlan;
    private bool _isExecuting = false;

    public SyncTreeControl(SyncPlanGenerator generator, SyncExecutor executor, ILogger<SyncTreeControl> logger)
    {
        InitializeComponent();
        _generator = generator;
        _executor = executor;
        _logger = logger;

        // Subscribe to executor progress events
        _executor.ProgressChanged += OnExecutorProgressChanged;

        // Initialize status icons
        InitializeStatusIcons();
    }

    private void InitializeStatusIcons()
    {
        var imageList = new ImageList();
        imageList.ImageSize = new Size(16, 16);
        imageList.ColorDepth = ColorDepth.Depth32Bit;

        // Create simple colored icons for different statuses
        imageList.Images.Add("pending", CreateStatusIcon(Color.Gray));
        imageList.Images.Add("selected", CreateStatusIcon(Color.Blue));
        imageList.Images.Add("running", CreateStatusIcon(Color.Orange));
        imageList.Images.Add("completed", CreateStatusIcon(Color.Green));
        imageList.Images.Add("failed", CreateStatusIcon(Color.Red));
        imageList.Images.Add("skipped", CreateStatusIcon(Color.LightGray));
        imageList.Images.Add("metadata_changed", CreateStatusIcon(Color.Yellow));
        imageList.Images.Add("has_dependencies", CreateStatusIcon(Color.Purple));

        uiTreeView.ImageList = imageList;

        // Wire up checkbox event
        uiTreeView.AfterCheck += OnNodeChecked;
        uiTreeView.AfterSelect += OnNodeSelected;
    }

    private Bitmap CreateStatusIcon(Color color)
    {
        var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (var brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, 2, 2, 12, 12);
            }
            using (var pen = new Pen(Color.Black, 1))
            {
                g.DrawEllipse(pen, 2, 2, 12, 12);
            }
        }
        return bitmap;
    }

    public async Task GeneratePlanAsync()
    {
        try
        {
            _logger.LogInformation("Генерация плана синхронизации...");
            
            // Disable buttons and show progress
            uiGenerateButton.Enabled = false;
            uiExecuteButton.Enabled = false;
            uiRefreshButton.Enabled = false;
            uiStatsLabel.Text = "Генерация плана...";
            uiTreeView.Nodes.Clear();

            _currentPlan = await _generator.GeneratePlanAsync();
            
            // Populate tree with generated plan
            PopulateTree(_currentPlan);

            // Update statistics and enable execute button
            UpdateStatistics();
            uiExecuteButton.Enabled = _currentPlan.TotalCount > 0;
            
            // Show preview mode indicator
            UpdateModeIndicator();

            _logger.LogInformation("План синхронизации успешно сгенерирован с {IntentCount} намерениями", _currentPlan.TotalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сгенерировать план синхронизации");
            MessageBox.Show($"Не удалось сгенерировать план: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            uiStatsLabel.Text = "Ошибка генерации плана";
        }
        finally
        {
            uiGenerateButton.Enabled = true;
            uiRefreshButton.Enabled = true;
        }
    }

    public async Task ExecuteSelectedAsync()
    {
        // Validate plan exists
        if (_currentPlan == null)
        {
            MessageBox.Show("Пожалуйста, сначала сгенерируйте план.", "Нет плана", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Validate at least one intent is selected
        var selectedCount = _currentPlan.SelectedCount;
        if (selectedCount == 0)
        {
            MessageBox.Show("Пожалуйста, выберите хотя бы одну операцию для выполнения.", "Нет выбора", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Show confirmation dialog before execution
        var result = MessageBox.Show(
            $"Выполнить {selectedCount} выбранных операций?\n\nЭто действие начнет загрузку и выгрузку файлов.",
            "Подтверждение выполнения",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Начало выполнения {SelectedCount} выбранных намерений", selectedCount);
            
            // Set execution mode
            _isExecuting = true;
            UpdateModeIndicator();
            
            // Disable execute button and enable cancel button
            uiExecuteButton.Enabled = false;
            uiGenerateButton.Enabled = false;
            uiRefreshButton.Enabled = false;
            uiCancelButton.Enabled = true;
            uiStatsLabel.Text = "Выполнение...";

            // Create progress reporter
            var progress = new Progress<ExecutionProgress>(p =>
            {
                if (InvokeRequired)
                {
                    Invoke(() => UpdateExecutionProgress(p));
                }
                else
                {
                    UpdateExecutionProgress(p);
                }
            });

            // Execute selected intents
            await _executor.ExecuteAsync(_currentPlan, progress);

            _logger.LogInformation("Выполнение завершено");
            MessageBox.Show("Выполнение завершено. Проверьте дерево для результатов.", "Выполнение завершено", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Выполнение не удалось");
            MessageBox.Show($"Выполнение не удалось: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            // Exit execution mode
            _isExecuting = false;
            UpdateModeIndicator();
            
            // Re-enable execute button after completion
            uiExecuteButton.Enabled = true;
            uiGenerateButton.Enabled = true;
            uiRefreshButton.Enabled = true;
            uiCancelButton.Enabled = false;
            
            // Update statistics and status label
            UpdateStatistics();
            
            // Check if execution was cancelled
            var cancelledCount = _currentPlan?.Intents.Count(i => i.Status == IntentStatus.Skipped) ?? 0;
            if (cancelledCount > 0)
            {
                uiStatsLabel.Text = $"Выполнение отменено. Всего: {_currentPlan?.TotalCount} | Завершено: {_currentPlan?.CompletedCount} | Пропущено: {cancelledCount}";
            }
        }
    }

    private void UpdateExecutionProgress(ExecutionProgress progress)
    {
        uiStatsLabel.Text = $"Executing: {progress.CompletedCount}/{progress.TotalCount} - {progress.Message}";

        if (progress.CurrentIntent != null)
        {
            UpdateNodeStatus(progress.CurrentIntent);
        }
    }

    private void OnExecutorProgressChanged(object? sender, IntentProgressEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateNodeStatus(e.Intent));
        }
        else
        {
            UpdateNodeStatus(e.Intent);
        }
    }

    private void UpdateNodeStatus(IntentObject intent)
    {
        // Find the tree node for this intent
        var node = FindIntentNode(uiTreeView.Nodes, intent);
        if (node == null)
        {
            return;
        }

        // Update node icon based on status
        var iconKey = GetStatusIconKey(intent.Status);
        node.ImageKey = iconKey;
        node.SelectedImageKey = iconKey;

        // Update node text color based on status
        node.ForeColor = GetStatusColor(intent.Status);

        // Add visual indicator for dependencies
        if (intent.Dependencies.Count > 0)
        {
            if (!node.Text.Contains("⚡"))
            {
                node.Text = $"⚡ {node.Text}";
            }
        }

        // Update parent nodes to reflect child status
        UpdateParentNodeStatus(node.Parent);

        // If this node is currently selected, update the log display
        if (uiTreeView.SelectedNode == node)
        {
            ShowExecutionLog(intent);
        }
    }

    private TreeNode? FindIntentNode(TreeNodeCollection nodes, IntentObject intent)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is IntentObject nodeIntent && nodeIntent.Id == intent.Id)
            {
                return node;
            }

            var childNode = FindIntentNode(node.Nodes, intent);
            if (childNode != null)
            {
                return childNode;
            }
        }

        return null;
    }

    private string GetStatusIconKey(IntentStatus status)
    {
        return status switch
        {
            IntentStatus.Pending => "pending",
            IntentStatus.Selected => "selected",
            IntentStatus.Running => "running",
            IntentStatus.Completed => "completed",
            IntentStatus.Failed => "failed",
            IntentStatus.Skipped => "skipped",
            _ => "pending"
        };
    }

    private Color GetStatusColor(IntentStatus status)
    {
        return status switch
        {
            IntentStatus.Pending => Color.Gray,
            IntentStatus.Selected => Color.Blue,
            IntentStatus.Running => Color.Orange,
            IntentStatus.Completed => Color.Green,
            IntentStatus.Failed => Color.Red,
            IntentStatus.Skipped => Color.LightGray,
            _ => Color.Black
        };
    }

    private void UpdateParentNodeStatus(TreeNode? parentNode)
    {
        if (parentNode == null)
        {
            return;
        }

        // Aggregate child statuses
        var hasRunning = false;
        var hasCompleted = false;
        var hasFailed = false;
        var hasSelected = false;

        foreach (TreeNode childNode in parentNode.Nodes)
        {
            if (childNode.Tag is IntentObject intent)
            {
                if (intent.Status == IntentStatus.Running) hasRunning = true;
                if (intent.Status == IntentStatus.Completed) hasCompleted = true;
                if (intent.Status == IntentStatus.Failed) hasFailed = true;
                if (intent.Status == IntentStatus.Selected) hasSelected = true;
            }
        }

        // Set parent icon based on aggregate status
        if (hasRunning)
        {
            parentNode.ImageKey = "running";
            parentNode.SelectedImageKey = "running";
        }
        else if (hasFailed)
        {
            parentNode.ImageKey = "failed";
            parentNode.SelectedImageKey = "failed";
        }
        else if (hasCompleted)
        {
            parentNode.ImageKey = "completed";
            parentNode.SelectedImageKey = "completed";
        }
        else if (hasSelected)
        {
            parentNode.ImageKey = "selected";
            parentNode.SelectedImageKey = "selected";
        }

        // Recursively update grandparent
        UpdateParentNodeStatus(parentNode.Parent);
    }

    private void OnNodeChecked(object? sender, TreeViewEventArgs e)
    {
        if (e.Node == null)
        {
            return;
        }

        // Prevent recursive calls during cascading updates
        uiTreeView.AfterCheck -= OnNodeChecked;

        try
        {
            var isChecked = e.Node.Checked;

            // Update IntentObject status if this is an intent node
            if (e.Node.Tag is IntentObject intent)
            {
                intent.Status = isChecked ? IntentStatus.Selected : IntentStatus.Pending;
                UpdateNodeStatus(intent);
                _logger.LogDebug("Intent {IntentId} status changed to {Status}", intent.Id, intent.Status);
            }

            // Cascade to children
            CheckAllChildren(e.Node, isChecked);

            // Update parent checkbox state
            UpdateParentCheckState(e.Node.Parent);

            // Update statistics
            UpdateStatistics();
        }
        finally
        {
            uiTreeView.AfterCheck += OnNodeChecked;
        }
    }

    private void CheckAllChildren(TreeNode node, bool isChecked)
    {
        foreach (TreeNode childNode in node.Nodes)
        {
            childNode.Checked = isChecked;

            // Update intent status
            if (childNode.Tag is IntentObject intent)
            {
                intent.Status = isChecked ? IntentStatus.Selected : IntentStatus.Pending;
                UpdateNodeStatus(intent);
            }

            // Recursively check grandchildren
            CheckAllChildren(childNode, isChecked);
        }
    }

    private void UpdateParentCheckState(TreeNode? parentNode)
    {
        if (parentNode == null)
        {
            return;
        }

        var checkedCount = 0;
        var totalCount = parentNode.Nodes.Count;

        foreach (TreeNode childNode in parentNode.Nodes)
        {
            if (childNode.Checked)
            {
                checkedCount++;
            }
        }

        // Set parent checkbox state based on children
        if (checkedCount == 0)
        {
            parentNode.Checked = false;
        }
        else if (checkedCount == totalCount)
        {
            parentNode.Checked = true;
        }
        else
        {
            // Partial selection - in WinForms TreeView, we can't show indeterminate state
            // but we keep the parent checked to indicate some children are selected
            parentNode.Checked = true;
        }

        // Recursively update grandparent
        UpdateParentCheckState(parentNode.Parent);
    }

    private void OnNodeSelected(object? sender, TreeViewEventArgs e)
    {
        if (e.Node == null)
        {
            return;
        }

        // Display metadata preview for intent nodes
        if (e.Node.Tag is IntentObject intent)
        {
            ShowMetadataPreview(intent);
            ShowExecutionLog(intent);
        }
        else if (e.Node.Tag is Media media)
        {
            ShowMediaMetadata(media);
            ClearExecutionLog();
        }
        else
        {
            ClearMetadataPreview();
            ClearExecutionLog();
        }

        _logger.LogDebug("Node selected: {NodeText}", e.Node.Text);
    }

    private void ShowMetadataPreview(IntentObject intent)
    {
        // Clear existing controls
        ClearMetadataPreview();

        if (intent.Media == null)
        {
            return;
        }

        var yPos = 40;
        var labelHeight = 20;
        var textBoxHeight = 60;
        var spacing = 10;

        // Current metadata section
        var currentLabel = new Label
        {
            Text = "Текущие метаданные:",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Location = new Point(10, yPos),
            AutoSize = true
        };
        uiMetadataPanel.Controls.Add(currentLabel);
        yPos += labelHeight + spacing;

        var currentMetadataText = FormatCurrentMetadata(intent);
        var currentTextBox = new TextBox
        {
            Text = currentMetadataText,
            Location = new Point(10, yPos),
            Size = new Size(uiMetadataPanel.Width - 30, textBoxHeight),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        uiMetadataPanel.Controls.Add(currentTextBox);
        yPos += textBoxHeight + spacing * 2;

        // Planned metadata section
        var plannedLabel = new Label
        {
            Text = "Планируемые метаданные:",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Location = new Point(10, yPos),
            AutoSize = true
        };
        uiMetadataPanel.Controls.Add(plannedLabel);
        yPos += labelHeight + spacing;

        var plannedMetadataText = FormatPlannedMetadata(intent);
        var plannedTextBox = new TextBox
        {
            Text = plannedMetadataText,
            Location = new Point(10, yPos),
            Size = new Size(uiMetadataPanel.Width - 30, textBoxHeight),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        uiMetadataPanel.Controls.Add(plannedTextBox);
        yPos += textBoxHeight + spacing * 2;

        // Changes section
        var changesLabel = new Label
        {
            Text = "Изменения:",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Location = new Point(10, yPos),
            AutoSize = true
        };
        uiMetadataPanel.Controls.Add(changesLabel);
        yPos += labelHeight + spacing;

        var changesText = GetMetadataChanges(intent);
        var changesTextBox = new TextBox
        {
            Text = changesText,
            Location = new Point(10, yPos),
            Size = new Size(uiMetadataPanel.Width - 30, textBoxHeight),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            ForeColor = string.IsNullOrEmpty(changesText) ? Color.Gray : Color.DarkOrange,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        uiMetadataPanel.Controls.Add(changesTextBox);
    }

    private void ShowMediaMetadata(Media media)
    {
        ClearMetadataPreview();

        var yPos = 40;
        var labelHeight = 20;
        var textBoxHeight = 100;
        var spacing = 10;

        var mediaLabel = new Label
        {
            Text = "Информация о медиа:",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Location = new Point(10, yPos),
            AutoSize = true
        };
        uiMetadataPanel.Controls.Add(mediaLabel);
        yPos += labelHeight + spacing;

        var mediaInfo = $"Название: {media.Title}\n" +
                       $"ID: {media.Id}\n" +
                       $"Описание: {media.Description}\n" +
                       $"Источников: {media.Sources.Count}";

        var mediaTextBox = new TextBox
        {
            Text = mediaInfo,
            Location = new Point(10, yPos),
            Size = new Size(uiMetadataPanel.Width - 30, textBoxHeight),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        uiMetadataPanel.Controls.Add(mediaTextBox);
    }

    private void ClearMetadataPreview()
    {
        // Remove all controls except the title label
        var controlsToRemove = uiMetadataPanel.Controls.Cast<Control>()
            .Where(c => c != uiMetadataLabel)
            .ToList();

        foreach (var control in controlsToRemove)
        {
            uiMetadataPanel.Controls.Remove(control);
            control.Dispose();
        }
    }

    private string FormatCurrentMetadata(IntentObject intent)
    {
        if (intent.Media == null)
        {
            return "Информация о медиа недоступна";
        }

        var media = intent.Media;
        var source = intent.Source;

        var metadata = $"Название: {media.Title}\n" +
                      $"Описание: {media.Description}\n" +
                      $"ID медиа: {media.Id}\n";

        if (source != null)
        {
            metadata += $"Источник: {source.Title}\n";
            var sourceLink = media.Sources.FirstOrDefault(s => s.SourceId == source.Id);
            if (sourceLink != null)
            {
                metadata += $"Внешний ID: {sourceLink.ExternalId}\n";
                metadata += $"Статус: {sourceLink.Status}\n";
            }
        }

        return metadata;
    }

    private string FormatPlannedMetadata(IntentObject intent)
    {
        if (intent.Media == null)
        {
            return "Информация о медиа недоступна";
        }

        var media = intent.Media;
        var target = intent.Target ?? intent.Source;

        var metadata = $"Название: {media.Title}\n" +
                      $"Описание: {media.Description}\n" +
                      $"ID медиа: {media.Id}\n";

        if (target != null)
        {
            metadata += $"Цель: {target.Title}\n";

            switch (intent.Type)
            {
                case IntentType.Download:
                    metadata += "Действие: Скачать в локальное хранилище\n";
                    break;
                case IntentType.Upload:
                    metadata += "Действие: Загрузить на целевую платформу\n";
                    metadata += "Примечание: Внешний ID будет назначен после загрузки\n";
                    break;
                case IntentType.UpdateStatus:
                    metadata += "Действие: Обновить статус в базе данных\n";
                    break;
                case IntentType.MarkAsDeleted:
                    metadata += "Действие: Пометить как удаленное\n";
                    break;
            }
        }

        return metadata;
    }

    private string GetMetadataChanges(IntentObject intent)
    {
        if (intent.Type == IntentType.Upload)
        {
            return "Метаданные могут быть изменены в соответствии с требованиями целевой платформы.\n" +
                   "Название и описание будут сохранены, где это возможно.";
        }
        else if (intent.Type == IntentType.UpdateStatus)
        {
            return "Статус будет обновлен в базе данных.";
        }
        else if (intent.Type == IntentType.MarkAsDeleted)
        {
            return "Ссылка на медиа будет удалена из источника.";
        }
        else if (intent.Type == IntentType.Download)
        {
            return "Изменения метаданных не ожидаются.";
        }

        return "Нет изменений";
    }

    private void ClearExecutionLog()
    {
        uiLogOutputTextBox.Clear();
    }

    private void ShowExecutionLog(IntentObject intent)
    {
        if (intent.LogOutput == null || intent.LogOutput.Length == 0)
        {
            uiLogOutputTextBox.Text = "Журнал выполнения недоступен.\n\n" +
                                     $"Статус: {intent.Status}\n" +
                                     $"Создано: {intent.CreatedAt:yyyy-MM-dd HH:mm:ss}\n";

            if (intent.ExecutedAt.HasValue)
            {
                uiLogOutputTextBox.AppendText($"Выполнено: {intent.ExecutedAt.Value:yyyy-MM-dd HH:mm:ss}\n");
            }

            if (!string.IsNullOrEmpty(intent.ErrorMessage))
            {
                uiLogOutputTextBox.AppendText($"\nОшибка: {intent.ErrorMessage}\n");
            }

            return;
        }

        // Display the log output
        var logText = $"=== Журнал выполнения для {intent.Type} ===\n";
        logText += $"Статус: {intent.Status}\n";
        logText += $"Создано: {intent.CreatedAt:yyyy-MM-dd HH:mm:ss}\n";

        if (intent.ExecutedAt.HasValue)
        {
            logText += $"Выполнено: {intent.ExecutedAt.Value:yyyy-MM-dd HH:mm:ss}\n";
        }

        logText += "\n--- Вывод ---\n";
        logText += intent.LogOutput.ToString();

        if (!string.IsNullOrEmpty(intent.ErrorMessage))
        {
            logText += $"\n\n--- Ошибка ---\n{intent.ErrorMessage}\n";
        }

        uiLogOutputTextBox.Text = logText;

        // Auto-scroll to bottom if the intent is running
        if (intent.Status == IntentStatus.Running)
        {
            uiLogOutputTextBox.SelectionStart = uiLogOutputTextBox.Text.Length;
            uiLogOutputTextBox.ScrollToCaret();
        }
    }

    private void uiGenerateButton_Click(object sender, EventArgs e)
    {
        _ = GeneratePlanAsync();
    }

    private void uiExecuteButton_Click(object sender, EventArgs e)
    {
        _ = ExecuteSelectedAsync();
    }

    private void uiCancelButton_Click(object sender, EventArgs e)
    {
        _logger.LogInformation("Cancellation requested by user");
        
        // Update UI to show cancellation in progress
        uiCancelButton.Enabled = false;
        uiStatsLabel.Text = "Отмена выполнения... Ожидание завершения текущих операций...";
        
        // Request cancellation from executor
        _executor.Cancel();
    }

    private void uiRefreshButton_Click(object sender, EventArgs e)
    {
        // Reset execution state
        _isExecuting = false;
        _ = GeneratePlanAsync();
    }

    private void UpdateStatistics()
    {
        if (_currentPlan == null)
        {
            uiStatsLabel.Text = "План не сгенерирован";
            return;
        }

        uiStatsLabel.Text = $"Всего: {_currentPlan.TotalCount} | Выбрано: {_currentPlan.SelectedCount} | " +
                           $"Завершено: {_currentPlan.CompletedCount} | Ошибок: {_currentPlan.FailedCount}";
    }

    private void UpdateModeIndicator()
    {
        if (_isExecuting)
        {
            // Execution mode - show clear indicator
            uiTopPanel.BackColor = Color.LightCoral;
            uiStatsLabel.ForeColor = Color.DarkRed;
            uiStatsLabel.Font = new Font(uiStatsLabel.Font, FontStyle.Bold);
        }
        else if (_currentPlan != null)
        {
            // Preview mode - show safe indicator
            uiTopPanel.BackColor = Color.LightGreen;
            uiStatsLabel.ForeColor = Color.DarkGreen;
            uiStatsLabel.Font = new Font(uiStatsLabel.Font, FontStyle.Bold);
        }
        else
        {
            // No plan - default state
            uiTopPanel.BackColor = SystemColors.Control;
            uiStatsLabel.ForeColor = SystemColors.ControlText;
            uiStatsLabel.Font = new Font(uiStatsLabel.Font, FontStyle.Regular);
        }
    }

    private void PopulateTree(SyncPlan plan)
    {
        _logger.LogInformation("Populating tree with {IntentCount} intents", plan.TotalCount);
        uiTreeView.BeginUpdate();
        uiTreeView.Nodes.Clear();

        try
        {
            // Group by Media for "Full Chain" view
            var sortedMediaIds = plan.IntentsByMedia.Keys
                .OrderBy(mediaId => plan.IntentsByMedia[mediaId].First().Media?.Title ?? "")
                .ToList();

            foreach (var mediaId in sortedMediaIds)
            {
                var mediaIntents = plan.IntentsByMedia[mediaId];
                var media = mediaIntents.First().Media;

                if (media == null)
                {
                    continue;
                }

                // Create media node (top level)
                var mediaNode = new TreeNode(media.Title);
                mediaNode.Tag = media;

                // Build dependency tree within this media
                // Root intents are those that don't depend on any other intent for THIS media
                var rootIntents = mediaIntents.Where(i => !i.Dependencies.Any(d => mediaIntents.Any(mi => mi.Id == d.Id))).ToList();

                foreach (var rootIntent in rootIntents)
                {
                    AddIntentNodeRecursive(mediaNode.Nodes, rootIntent, mediaIntents);
                }

                uiTreeView.Nodes.Add(mediaNode);
            }

            // Expand first level by default
            foreach (TreeNode node in uiTreeView.Nodes)
            {
                node.Expand();
            }

            _logger.LogInformation("Tree populated with {MediaCount} media items", uiTreeView.Nodes.Count);
        }
        finally
        {
            uiTreeView.EndUpdate();
        }
    }

    private void AddIntentNodeRecursive(TreeNodeCollection nodes, IntentObject intent, List<IntentObject> allMediaIntents)
    {
        var intentText = GetIntentText(intent);
        var intentNode = new TreeNode(intentText);
        intentNode.Tag = intent;

        // Set initial status icon
        var iconKey = GetStatusIconKey(intent.Status);
        intentNode.ImageKey = iconKey;
        intentNode.SelectedImageKey = iconKey;
        intentNode.ForeColor = GetStatusColor(intent.Status);

        // Add visual indicator for dependencies
        if (intent.Dependencies.Count > 0)
        {
            intentNode.Text = $"⚡ {intentNode.Text}";
        }

        // Add visual indicator for metadata changes
        if (WillMetadataChange(intent))
        {
            intentNode.Text = $"📝 {intentNode.Text}";
        }

        nodes.Add(intentNode);

        // Find intents that depend on THIS intent
        var dependents = allMediaIntents.Where(i => i.Dependencies.Any(d => d.Id == intent.Id)).ToList();
        foreach (var dependent in dependents)
        {
            AddIntentNodeRecursive(intentNode.Nodes, dependent, allMediaIntents);
        }
    }

    private string GetRelationText(IntentObject intent)
    {
        var sourceName = intent.Source?.Title ?? "Неизвестный источник";
        var targetName = intent.Target?.Title ?? "Неизвестная цель";

        // For intents with only source (UpdateStatus, MarkAsDeleted), show source only
        if (intent.Target == null && intent.Source != null)
        {
            return $"{sourceName}";
        }

        return $"{sourceName} → {targetName}";
    }

    private string GetIntentText(IntentObject intent)
    {
        var mediaTitle = intent.Media?.Title ?? "Неизвестное медиа";
        var operationType = intent.Type.ToString();
        var impactInfo = GetEstimatedImpact(intent);

        switch (intent.Type)
        {
            case IntentType.Download:
                var sourceName = intent.Source?.Title ?? "Неизвестно";
                return $"Скачать из {sourceName}{impactInfo}";

            case IntentType.Upload:
                var targetName = intent.Target?.Title ?? "Неизвестно";
                return $"Загрузить в {targetName}{impactInfo}";

            case IntentType.UpdateStatus:
                var updateSourceName = intent.Source?.Title ?? "Неизвестно";
                return $"Обновить статус в {updateSourceName}";

            case IntentType.MarkAsDeleted:
                var deleteSourceName = intent.Source?.Title ?? "Неизвестно";
                return $"Пометить как удаленное в {deleteSourceName}";

            default:
                return $"{operationType}";
        }
    }

    private string GetEstimatedImpact(IntentObject intent)
    {
        // For download and upload operations, show estimated impact
        if (intent.Type == IntentType.Download || intent.Type == IntentType.Upload)
        {
            // Try to get file size from media metadata
            // This is a placeholder - in real implementation, you would query the source
            // for actual file size or use cached metadata
            
            // For now, show a generic indicator
            return " [~размер файла неизвестен]";
        }

        return string.Empty;
    }

    private bool WillMetadataChange(IntentObject intent)
    {
        // For Upload intents, metadata might change based on target platform requirements
        // This is a simplified check - in a real implementation, you would compare
        // current metadata with planned metadata
        if (intent.Type == IntentType.Upload)
        {
            // Placeholder: assume metadata might change for uploads
            return true;
        }

        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_executor != null)
            {
                _executor.ProgressChanged -= OnExecutorProgressChanged;
            }
            components?.Dispose();
        }
        base.Dispose(disposing);
    }
}
