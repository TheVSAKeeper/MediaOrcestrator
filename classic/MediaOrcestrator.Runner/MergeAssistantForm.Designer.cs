namespace MediaOrcestrator.Runner;

partial class MergeAssistantForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        uiMainLayout = new TableLayoutPanel();
        uiScopePanel = new TableLayoutPanel();
        uiScopeLabel = new Label();
        uiSourceScopeList = new CheckedListBox();
        uiScopeButtonsPanel = new FlowLayoutPanel();
        uiFindCandidatesButton = new Button();
        uiSelectAllButton = new Button();
        uiSelectNoneButton = new Button();
        uiGroupsGrid = new DataGridView();
        uiCheckColumn = new DataGridViewCheckBoxColumn();
        uiKeyColumn = new DataGridViewTextBoxColumn();
        uiTargetColumn = new DataGridViewTextBoxColumn();
        uiMergingColumn = new DataGridViewTextBoxColumn();
        uiSourcesAfterColumn = new DataGridViewTextBoxColumn();
        uiStatusLabel = new Label();
        uiButtonPanel = new FlowLayoutPanel();
        uiCancelButton = new Button();
        uiApplyButton = new Button();
        uiMainLayout.SuspendLayout();
        uiScopePanel.SuspendLayout();
        uiScopeButtonsPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)uiGroupsGrid).BeginInit();
        uiButtonPanel.SuspendLayout();
        SuspendLayout();
        // 
        // uiMainLayout
        // 
        uiMainLayout.ColumnCount = 1;
        uiMainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        uiMainLayout.Controls.Add(uiScopePanel, 0, 0);
        uiMainLayout.Controls.Add(uiGroupsGrid, 0, 1);
        uiMainLayout.Controls.Add(uiStatusLabel, 0, 2);
        uiMainLayout.Controls.Add(uiButtonPanel, 0, 3);
        uiMainLayout.Dock = DockStyle.Fill;
        uiMainLayout.Location = new Point(0, 0);
        uiMainLayout.Name = "uiMainLayout";
        uiMainLayout.Padding = new Padding(10);
        uiMainLayout.RowCount = 4;
        uiMainLayout.RowStyles.Add(new RowStyle());
        uiMainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        uiMainLayout.RowStyles.Add(new RowStyle());
        uiMainLayout.RowStyles.Add(new RowStyle());
        uiMainLayout.Size = new Size(946, 655);
        uiMainLayout.TabIndex = 0;
        // 
        // uiScopePanel
        // 
        uiScopePanel.AutoSize = true;
        uiScopePanel.ColumnCount = 3;
        uiScopePanel.ColumnStyles.Add(new ColumnStyle());
        uiScopePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        uiScopePanel.ColumnStyles.Add(new ColumnStyle());
        uiScopePanel.Controls.Add(uiScopeLabel, 0, 0);
        uiScopePanel.Controls.Add(uiSourceScopeList, 1, 0);
        uiScopePanel.Controls.Add(uiScopeButtonsPanel, 2, 0);
        uiScopePanel.Dock = DockStyle.Fill;
        uiScopePanel.Location = new Point(13, 13);
        uiScopePanel.Name = "uiScopePanel";
        uiScopePanel.RowCount = 1;
        uiScopePanel.RowStyles.Add(new RowStyle());
        uiScopePanel.Size = new Size(920, 106);
        uiScopePanel.TabIndex = 0;
        // 
        // uiScopeLabel
        // 
        uiScopeLabel.AutoSize = true;
        uiScopeLabel.Location = new Point(3, 6);
        uiScopeLabel.Margin = new Padding(3, 6, 3, 0);
        uiScopeLabel.Name = "uiScopeLabel";
        uiScopeLabel.Size = new Size(71, 15);
        uiScopeLabel.TabIndex = 0;
        uiScopeLabel.Text = "Источники:";
        // 
        // uiSourceScopeList
        // 
        uiSourceScopeList.CheckOnClick = true;
        uiSourceScopeList.ColumnWidth = 180;
        uiSourceScopeList.Dock = DockStyle.Fill;
        uiSourceScopeList.Location = new Point(80, 3);
        uiSourceScopeList.MultiColumn = true;
        uiSourceScopeList.Name = "uiSourceScopeList";
        uiSourceScopeList.Size = new Size(665, 100);
        uiSourceScopeList.TabIndex = 0;
        // 
        // uiScopeButtonsPanel
        // 
        uiScopeButtonsPanel.AutoSize = true;
        uiScopeButtonsPanel.Controls.Add(uiFindCandidatesButton);
        uiScopeButtonsPanel.Controls.Add(uiSelectAllButton);
        uiScopeButtonsPanel.Controls.Add(uiSelectNoneButton);
        uiScopeButtonsPanel.FlowDirection = FlowDirection.TopDown;
        uiScopeButtonsPanel.Location = new Point(751, 3);
        uiScopeButtonsPanel.Name = "uiScopeButtonsPanel";
        uiScopeButtonsPanel.Size = new Size(166, 99);
        uiScopeButtonsPanel.TabIndex = 1;
        // 
        // uiFindCandidatesButton
        // 
        uiFindCandidatesButton.AutoSize = true;
        uiFindCandidatesButton.Location = new Point(3, 3);
        uiFindCandidatesButton.Name = "uiFindCandidatesButton";
        uiFindCandidatesButton.Size = new Size(160, 27);
        uiFindCandidatesButton.TabIndex = 0;
        uiFindCandidatesButton.Text = "Найти кандидатов";
        uiFindCandidatesButton.UseVisualStyleBackColor = true;
        uiFindCandidatesButton.Click += uiFindCandidatesButton_Click;
        // 
        // uiSelectAllButton
        // 
        uiSelectAllButton.AutoSize = true;
        uiSelectAllButton.Location = new Point(3, 36);
        uiSelectAllButton.Name = "uiSelectAllButton";
        uiSelectAllButton.Size = new Size(160, 27);
        uiSelectAllButton.TabIndex = 1;
        uiSelectAllButton.Text = "Отметить все";
        uiSelectAllButton.UseVisualStyleBackColor = true;
        uiSelectAllButton.Click += uiSelectAllButton_Click;
        // 
        // uiSelectNoneButton
        // 
        uiSelectNoneButton.AutoSize = true;
        uiSelectNoneButton.Location = new Point(3, 69);
        uiSelectNoneButton.Name = "uiSelectNoneButton";
        uiSelectNoneButton.Size = new Size(160, 27);
        uiSelectNoneButton.TabIndex = 2;
        uiSelectNoneButton.Text = "Снять все";
        uiSelectNoneButton.UseVisualStyleBackColor = true;
        uiSelectNoneButton.Click += uiSelectNoneButton_Click;
        // 
        // uiGroupsGrid
        // 
        uiGroupsGrid.AllowUserToAddRows = false;
        uiGroupsGrid.AllowUserToDeleteRows = false;
        uiGroupsGrid.AllowUserToResizeRows = false;
        uiGroupsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        uiGroupsGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        uiGroupsGrid.Columns.AddRange(new DataGridViewColumn[] { uiCheckColumn, uiKeyColumn, uiTargetColumn, uiMergingColumn, uiSourcesAfterColumn });
        uiGroupsGrid.Dock = DockStyle.Fill;
        uiGroupsGrid.Location = new Point(13, 125);
        uiGroupsGrid.Name = "uiGroupsGrid";
        uiGroupsGrid.RowHeadersVisible = false;
        uiGroupsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        uiGroupsGrid.Size = new Size(920, 463);
        uiGroupsGrid.TabIndex = 1;
        uiGroupsGrid.CellValueChanged += uiGroupsGrid_CellValueChanged;
        uiGroupsGrid.CurrentCellDirtyStateChanged += uiGroupsGrid_CurrentCellDirtyStateChanged;
        // 
        // uiCheckColumn
        // 
        uiCheckColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        uiCheckColumn.FillWeight = 6F;
        uiCheckColumn.HeaderText = "";
        uiCheckColumn.Name = "uiCheckColumn";
        uiCheckColumn.Resizable = DataGridViewTriState.False;
        uiCheckColumn.Width = 40;
        // 
        // uiKeyColumn
        // 
        uiKeyColumn.FillWeight = 22F;
        uiKeyColumn.HeaderText = "Нормализованное имя";
        uiKeyColumn.Name = "uiKeyColumn";
        uiKeyColumn.ReadOnly = true;
        // 
        // uiTargetColumn
        // 
        uiTargetColumn.FillWeight = 32F;
        uiTargetColumn.HeaderText = "Целевое";
        uiTargetColumn.Name = "uiTargetColumn";
        uiTargetColumn.ReadOnly = true;
        // 
        // uiMergingColumn
        // 
        uiMergingColumn.FillWeight = 30F;
        uiMergingColumn.HeaderText = "К присоединению";
        uiMergingColumn.Name = "uiMergingColumn";
        uiMergingColumn.ReadOnly = true;
        // 
        // uiSourcesAfterColumn
        // 
        uiSourcesAfterColumn.FillWeight = 10F;
        uiSourcesAfterColumn.HeaderText = "Источников после";
        uiSourcesAfterColumn.Name = "uiSourcesAfterColumn";
        uiSourcesAfterColumn.ReadOnly = true;
        // 
        // uiStatusLabel
        // 
        uiStatusLabel.AutoSize = true;
        uiStatusLabel.Dock = DockStyle.Fill;
        uiStatusLabel.Location = new Point(13, 591);
        uiStatusLabel.Name = "uiStatusLabel";
        uiStatusLabel.Size = new Size(920, 15);
        uiStatusLabel.TabIndex = 2;
        uiStatusLabel.Text = "Нажмите «Найти кандидатов», чтобы начать";
        // 
        // uiButtonPanel
        // 
        uiButtonPanel.AutoSize = true;
        uiButtonPanel.Controls.Add(uiCancelButton);
        uiButtonPanel.Controls.Add(uiApplyButton);
        uiButtonPanel.Dock = DockStyle.Fill;
        uiButtonPanel.FlowDirection = FlowDirection.RightToLeft;
        uiButtonPanel.Location = new Point(13, 609);
        uiButtonPanel.Name = "uiButtonPanel";
        uiButtonPanel.Size = new Size(920, 33);
        uiButtonPanel.TabIndex = 3;
        // 
        // uiCancelButton
        // 
        uiCancelButton.DialogResult = DialogResult.Cancel;
        uiCancelButton.Location = new Point(817, 3);
        uiCancelButton.Name = "uiCancelButton";
        uiCancelButton.Size = new Size(100, 27);
        uiCancelButton.TabIndex = 0;
        uiCancelButton.Text = "Закрыть";
        uiCancelButton.UseVisualStyleBackColor = true;
        // 
        // uiApplyButton
        // 
        uiApplyButton.Enabled = false;
        uiApplyButton.Location = new Point(631, 3);
        uiApplyButton.Name = "uiApplyButton";
        uiApplyButton.Size = new Size(180, 27);
        uiApplyButton.TabIndex = 1;
        uiApplyButton.Text = "Объединить отмеченные";
        uiApplyButton.UseVisualStyleBackColor = true;
        uiApplyButton.Click += uiApplyButton_Click;
        // 
        // MergeAssistantForm
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = uiCancelButton;
        ClientSize = new Size(946, 655);
        Controls.Add(uiMainLayout);
        MinimumSize = new Size(700, 450);
        Name = "MergeAssistantForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Ассистент объединения";
        uiMainLayout.ResumeLayout(false);
        uiMainLayout.PerformLayout();
        uiScopePanel.ResumeLayout(false);
        uiScopePanel.PerformLayout();
        uiScopeButtonsPanel.ResumeLayout(false);
        uiScopeButtonsPanel.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)uiGroupsGrid).EndInit();
        uiButtonPanel.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel uiMainLayout;
    private TableLayoutPanel uiScopePanel;
    private Label uiScopeLabel;
    private CheckedListBox uiSourceScopeList;
    private FlowLayoutPanel uiScopeButtonsPanel;
    private Button uiFindCandidatesButton;
    private Button uiSelectAllButton;
    private Button uiSelectNoneButton;
    private DataGridView uiGroupsGrid;
    private DataGridViewCheckBoxColumn uiCheckColumn;
    private DataGridViewTextBoxColumn uiKeyColumn;
    private DataGridViewTextBoxColumn uiTargetColumn;
    private DataGridViewTextBoxColumn uiMergingColumn;
    private DataGridViewTextBoxColumn uiSourcesAfterColumn;
    private Label uiStatusLabel;
    private FlowLayoutPanel uiButtonPanel;
    private Button uiCancelButton;
    private Button uiApplyButton;
}
