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
        uiToolsGrid = new DataGridView();
        uiColName = new DataGridViewTextBoxColumn();
        uiColInstalled = new DataGridViewTextBoxColumn();
        uiColLatest = new DataGridViewTextBoxColumn();
        uiColStatus = new DataGridViewTextBoxColumn();
        uiColAction = new DataGridViewButtonColumn();
        uiCheckUpdatesButton = new Button();
        uiStatusLabel = new Label();
        uiProgressBar = new ProgressBar();
        ((System.ComponentModel.ISupportInitialize)uiToolsGrid).BeginInit();
        SuspendLayout();
        //
        // uiToolsGrid
        //
        uiToolsGrid.AllowUserToAddRows = false;
        uiToolsGrid.AllowUserToDeleteRows = false;
        uiToolsGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        uiToolsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        uiToolsGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        uiToolsGrid.Columns.AddRange(new DataGridViewColumn[] { uiColName, uiColInstalled, uiColLatest, uiColStatus, uiColAction });
        uiToolsGrid.Location = new Point(12, 12);
        uiToolsGrid.Name = "uiToolsGrid";
        uiToolsGrid.ReadOnly = true;
        uiToolsGrid.RowHeadersVisible = false;
        uiToolsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        uiToolsGrid.ShowCellToolTips = true;
        uiToolsGrid.Size = new Size(560, 200);
        uiToolsGrid.TabIndex = 0;
        uiToolsGrid.CellClick += ToolsDataGridView_CellClick;
        //
        // uiColName
        //
        uiColName.FillWeight = 25F;
        uiColName.HeaderText = "Инструмент";
        uiColName.Name = "uiColName";
        uiColName.ReadOnly = true;
        //
        // uiColInstalled
        //
        uiColInstalled.FillWeight = 25F;
        uiColInstalled.HeaderText = "Установлена";
        uiColInstalled.Name = "uiColInstalled";
        uiColInstalled.ReadOnly = true;
        //
        // uiColLatest
        //
        uiColLatest.FillWeight = 25F;
        uiColLatest.HeaderText = "Доступна";
        uiColLatest.Name = "uiColLatest";
        uiColLatest.ReadOnly = true;
        //
        // uiColStatus
        //
        uiColStatus.FillWeight = 25F;
        uiColStatus.HeaderText = "Статус";
        uiColStatus.Name = "uiColStatus";
        uiColStatus.ReadOnly = true;
        //
        // uiColAction
        //
        uiColAction.FillWeight = 20F;
        uiColAction.HeaderText = "";
        uiColAction.Name = "uiColAction";
        uiColAction.ReadOnly = true;
        uiColAction.Text = "";
        //
        // uiCheckUpdatesButton
        //
        uiCheckUpdatesButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        uiCheckUpdatesButton.Location = new Point(12, 220);
        uiCheckUpdatesButton.Name = "uiCheckUpdatesButton";
        uiCheckUpdatesButton.Size = new Size(180, 30);
        uiCheckUpdatesButton.TabIndex = 1;
        uiCheckUpdatesButton.Text = "Проверить обновления";
        uiCheckUpdatesButton.UseVisualStyleBackColor = true;
        uiCheckUpdatesButton.Click += CheckUpdatesButton_Click;
        //
        // uiStatusLabel
        //
        uiStatusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        uiStatusLabel.Location = new Point(12, 258);
        uiStatusLabel.Name = "uiStatusLabel";
        uiStatusLabel.Size = new Size(560, 20);
        uiStatusLabel.TabIndex = 3;
        //
        // uiProgressBar
        //
        uiProgressBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        uiProgressBar.Location = new Point(200, 225);
        uiProgressBar.Name = "uiProgressBar";
        uiProgressBar.Size = new Size(270, 20);
        uiProgressBar.TabIndex = 2;
        uiProgressBar.Visible = false;
        //
        // ToolsForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(584, 281);
        Controls.Add(uiToolsGrid);
        Controls.Add(uiCheckUpdatesButton);
        Controls.Add(uiProgressBar);
        Controls.Add(uiStatusLabel);
        MinimumSize = new Size(500, 300);
        Name = "ToolsForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Управление инструментами";
        ((System.ComponentModel.ISupportInitialize)uiToolsGrid).EndInit();
        ResumeLayout(false);
    }

    private DataGridView uiToolsGrid;
    private DataGridViewTextBoxColumn uiColName;
    private DataGridViewTextBoxColumn uiColInstalled;
    private DataGridViewTextBoxColumn uiColLatest;
    private DataGridViewTextBoxColumn uiColStatus;
    private DataGridViewButtonColumn uiColAction;
    private Button uiCheckUpdatesButton;
    private Label uiStatusLabel;
    private ProgressBar uiProgressBar;
}
