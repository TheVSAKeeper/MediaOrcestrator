namespace MediaOrcestrator.Runner;

partial class ToolsForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        toolsDataGridView = new DataGridView();
        colName = new DataGridViewTextBoxColumn();
        colInstalled = new DataGridViewTextBoxColumn();
        colLatest = new DataGridViewTextBoxColumn();
        colStatus = new DataGridViewTextBoxColumn();
        colAction = new DataGridViewButtonColumn();
        checkUpdatesButton = new Button();
        statusLabel = new Label();
        progressBar = new ProgressBar();
        ((System.ComponentModel.ISupportInitialize)toolsDataGridView).BeginInit();
        SuspendLayout();
        // 
        // toolsDataGridView
        // 
        toolsDataGridView.AllowUserToAddRows = false;
        toolsDataGridView.AllowUserToDeleteRows = false;
        toolsDataGridView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        toolsDataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        toolsDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        toolsDataGridView.Columns.AddRange(new DataGridViewColumn[] { colName, colInstalled, colLatest, colStatus, colAction });
        toolsDataGridView.Location = new Point(12, 12);
        toolsDataGridView.Name = "toolsDataGridView";
        toolsDataGridView.ReadOnly = true;
        toolsDataGridView.RowHeadersVisible = false;
        toolsDataGridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        toolsDataGridView.Size = new Size(560, 200);
        toolsDataGridView.TabIndex = 0;
        toolsDataGridView.CellClick += ToolsDataGridView_CellClick;
        // 
        // colName
        // 
        colName.FillWeight = 25F;
        colName.HeaderText = "Инструмент";
        colName.Name = "colName";
        colName.ReadOnly = true;
        // 
        // colInstalled
        // 
        colInstalled.FillWeight = 25F;
        colInstalled.HeaderText = "Установлена";
        colInstalled.Name = "colInstalled";
        colInstalled.ReadOnly = true;
        // 
        // colLatest
        // 
        colLatest.FillWeight = 25F;
        colLatest.HeaderText = "Доступна";
        colLatest.Name = "colLatest";
        colLatest.ReadOnly = true;
        // 
        // colStatus
        // 
        colStatus.FillWeight = 25F;
        colStatus.HeaderText = "Статус";
        colStatus.Name = "colStatus";
        colStatus.ReadOnly = true;
        // 
        // colAction
        // 
        colAction.FillWeight = 20F;
        colAction.HeaderText = "";
        colAction.Name = "colAction";
        colAction.ReadOnly = true;
        colAction.Text = "";
        // 
        // checkUpdatesButton
        // 
        checkUpdatesButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        checkUpdatesButton.Location = new Point(12, 220);
        checkUpdatesButton.Name = "checkUpdatesButton";
        checkUpdatesButton.Size = new Size(180, 30);
        checkUpdatesButton.TabIndex = 1;
        checkUpdatesButton.Text = "Проверить обновления";
        checkUpdatesButton.UseVisualStyleBackColor = true;
        checkUpdatesButton.Click += CheckUpdatesButton_Click;
        // 
        // statusLabel
        // 
        statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        statusLabel.Location = new Point(12, 258);
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(560, 20);
        statusLabel.TabIndex = 3;
        // 
        // progressBar
        // 
        progressBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        progressBar.Location = new Point(200, 225);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(270, 20);
        progressBar.TabIndex = 2;
        progressBar.Visible = false;
        // 
        // ToolsForm
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(584, 281);
        Controls.Add(toolsDataGridView);
        Controls.Add(checkUpdatesButton);
        Controls.Add(progressBar);
        Controls.Add(statusLabel);
        MinimumSize = new Size(500, 300);
        Name = "ToolsForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Управление инструментами";
        ((System.ComponentModel.ISupportInitialize)toolsDataGridView).EndInit();
        ResumeLayout(false);
    }

    private DataGridView toolsDataGridView;
    private DataGridViewTextBoxColumn colName;
    private DataGridViewTextBoxColumn colInstalled;
    private DataGridViewTextBoxColumn colLatest;
    private DataGridViewTextBoxColumn colStatus;
    private DataGridViewButtonColumn colAction;
    private Button checkUpdatesButton;
    private Label statusLabel;
    private ProgressBar progressBar;
}
