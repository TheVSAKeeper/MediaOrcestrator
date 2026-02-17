namespace MediaOrcestrator.Runner
{
    partial class SyncTreeControl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            uiMainLayout = new TableLayoutPanel();
            uiTopPanel = new Panel();
            uiRefreshButton = new Button();
            uiCancelButton = new Button();
            uiExecuteButton = new Button();
            uiGenerateButton = new Button();
            uiStatsLabel = new Label();
            uiContentSplitter = new SplitContainer();
            uiTreeView = new TreeView();
            uiRightSplitter = new SplitContainer();
            uiMetadataPanel = new Panel();
            uiMetadataLabel = new Label();
            uiLogPanel = new Panel();
            uiLogLabel = new Label();
            uiLogOutputTextBox = new TextBox();
            uiMainLayout.SuspendLayout();
            uiTopPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)uiContentSplitter).BeginInit();
            uiContentSplitter.Panel1.SuspendLayout();
            uiContentSplitter.Panel2.SuspendLayout();
            uiContentSplitter.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)uiRightSplitter).BeginInit();
            uiRightSplitter.Panel1.SuspendLayout();
            uiRightSplitter.Panel2.SuspendLayout();
            uiRightSplitter.SuspendLayout();
            uiMetadataPanel.SuspendLayout();
            uiLogPanel.SuspendLayout();
            SuspendLayout();
            // 
            // uiMainLayout
            // 
            uiMainLayout.ColumnCount = 1;
            uiMainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            uiMainLayout.Controls.Add(uiTopPanel, 0, 0);
            uiMainLayout.Controls.Add(uiContentSplitter, 0, 1);
            uiMainLayout.Dock = DockStyle.Fill;
            uiMainLayout.Location = new Point(0, 0);
            uiMainLayout.Name = "uiMainLayout";
            uiMainLayout.RowCount = 2;
            uiMainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));
            uiMainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            uiMainLayout.Size = new Size(1200, 800);
            uiMainLayout.TabIndex = 0;
            // 
            // uiTopPanel
            // 
            uiTopPanel.Controls.Add(uiRefreshButton);
            uiTopPanel.Controls.Add(uiCancelButton);
            uiTopPanel.Controls.Add(uiExecuteButton);
            uiTopPanel.Controls.Add(uiGenerateButton);
            uiTopPanel.Controls.Add(uiStatsLabel);
            uiTopPanel.Dock = DockStyle.Fill;
            uiTopPanel.Location = new Point(3, 3);
            uiTopPanel.Name = "uiTopPanel";
            uiTopPanel.Size = new Size(1194, 74);
            uiTopPanel.TabIndex = 0;
            // 
            // uiRefreshButton
            // 
            uiRefreshButton.Location = new Point(330, 10);
            uiRefreshButton.Name = "uiRefreshButton";
            uiRefreshButton.Size = new Size(100, 30);
            uiRefreshButton.TabIndex = 3;
            uiRefreshButton.Text = "Обновить";
            uiRefreshButton.UseVisualStyleBackColor = true;
            uiRefreshButton.Click += uiRefreshButton_Click;
            // 
            // uiCancelButton
            // 
            uiCancelButton.Enabled = false;
            uiCancelButton.Location = new Point(220, 10);
            uiCancelButton.Name = "uiCancelButton";
            uiCancelButton.Size = new Size(100, 30);
            uiCancelButton.TabIndex = 2;
            uiCancelButton.Text = "Отменить";
            uiCancelButton.UseVisualStyleBackColor = true;
            uiCancelButton.Click += uiCancelButton_Click;
            // 
            // uiExecuteButton
            // 
            uiExecuteButton.Enabled = false;
            uiExecuteButton.Location = new Point(110, 10);
            uiExecuteButton.Name = "uiExecuteButton";
            uiExecuteButton.Size = new Size(100, 30);
            uiExecuteButton.TabIndex = 1;
            uiExecuteButton.Text = "Выполнить";
            uiExecuteButton.UseVisualStyleBackColor = true;
            uiExecuteButton.Click += uiExecuteButton_Click;
            // 
            // uiGenerateButton
            // 
            uiGenerateButton.Location = new Point(10, 10);
            uiGenerateButton.Name = "uiGenerateButton";
            uiGenerateButton.Size = new Size(100, 30);
            uiGenerateButton.TabIndex = 0;
            uiGenerateButton.Text = "Создать план";
            uiGenerateButton.UseVisualStyleBackColor = true;
            uiGenerateButton.Click += uiGenerateButton_Click;
            // 
            // uiStatsLabel
            // 
            uiStatsLabel.AutoSize = true;
            uiStatsLabel.Location = new Point(10, 50);
            uiStatsLabel.Name = "uiStatsLabel";
            uiStatsLabel.Size = new Size(130, 15);
            uiStatsLabel.TabIndex = 4;
            uiStatsLabel.Text = "План не сгенерирован";
            // 
            // uiContentSplitter
            // 
            uiContentSplitter.Dock = DockStyle.Fill;
            uiContentSplitter.Location = new Point(3, 83);
            uiContentSplitter.Name = "uiContentSplitter";
            // 
            // uiContentSplitter.Panel1
            // 
            uiContentSplitter.Panel1.Controls.Add(uiTreeView);
            // 
            // uiContentSplitter.Panel2
            // 
            uiContentSplitter.Panel2.Controls.Add(uiRightSplitter);
            uiContentSplitter.Size = new Size(1194, 714);
            uiContentSplitter.SplitterDistance = 600;
            uiContentSplitter.TabIndex = 1;
            // 
            // uiTreeView
            // 
            uiTreeView.CheckBoxes = true;
            uiTreeView.Dock = DockStyle.Fill;
            uiTreeView.Location = new Point(0, 0);
            uiTreeView.Name = "uiTreeView";
            uiTreeView.Size = new Size(600, 714);
            uiTreeView.TabIndex = 0;
            // 
            // uiRightSplitter
            // 
            uiRightSplitter.Dock = DockStyle.Fill;
            uiRightSplitter.Location = new Point(0, 0);
            uiRightSplitter.Name = "uiRightSplitter";
            uiRightSplitter.Orientation = Orientation.Horizontal;
            // 
            // uiRightSplitter.Panel1
            // 
            uiRightSplitter.Panel1.Controls.Add(uiMetadataPanel);
            // 
            // uiRightSplitter.Panel2
            // 
            uiRightSplitter.Panel2.Controls.Add(uiLogPanel);
            uiRightSplitter.Size = new Size(590, 714);
            uiRightSplitter.SplitterDistance = 357;
            uiRightSplitter.TabIndex = 0;
            // 
            // uiMetadataPanel
            // 
            uiMetadataPanel.Controls.Add(uiMetadataLabel);
            uiMetadataPanel.Dock = DockStyle.Fill;
            uiMetadataPanel.Location = new Point(0, 0);
            uiMetadataPanel.Name = "uiMetadataPanel";
            uiMetadataPanel.Size = new Size(590, 357);
            uiMetadataPanel.TabIndex = 0;
            // 
            // uiMetadataLabel
            // 
            uiMetadataLabel.AutoSize = true;
            uiMetadataLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            uiMetadataLabel.Location = new Point(10, 10);
            uiMetadataLabel.Name = "uiMetadataLabel";
            uiMetadataLabel.Size = new Size(150, 19);
            uiMetadataLabel.TabIndex = 0;
            uiMetadataLabel.Text = "Предпросмотр метаданных";
            // 
            // uiLogPanel
            // 
            uiLogPanel.Controls.Add(uiLogLabel);
            uiLogPanel.Controls.Add(uiLogOutputTextBox);
            uiLogPanel.Dock = DockStyle.Fill;
            uiLogPanel.Location = new Point(0, 0);
            uiLogPanel.Name = "uiLogPanel";
            uiLogPanel.Size = new Size(590, 353);
            uiLogPanel.TabIndex = 0;
            // 
            // uiLogLabel
            // 
            uiLogLabel.AutoSize = true;
            uiLogLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            uiLogLabel.Location = new Point(10, 10);
            uiLogLabel.Name = "uiLogLabel";
            uiLogLabel.Size = new Size(110, 19);
            uiLogLabel.TabIndex = 0;
            uiLogLabel.Text = "Журнал выполнения";
            // 
            // uiLogOutputTextBox
            // 
            uiLogOutputTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            uiLogOutputTextBox.Font = new Font("Consolas", 9F);
            uiLogOutputTextBox.Location = new Point(10, 35);
            uiLogOutputTextBox.Multiline = true;
            uiLogOutputTextBox.Name = "uiLogOutputTextBox";
            uiLogOutputTextBox.ReadOnly = true;
            uiLogOutputTextBox.ScrollBars = ScrollBars.Both;
            uiLogOutputTextBox.Size = new Size(570, 308);
            uiLogOutputTextBox.TabIndex = 1;
            uiLogOutputTextBox.WordWrap = false;
            // 
            // SyncTreeControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(uiMainLayout);
            Name = "SyncTreeControl";
            Size = new Size(1200, 800);
            uiMainLayout.ResumeLayout(false);
            uiTopPanel.ResumeLayout(false);
            uiTopPanel.PerformLayout();
            uiContentSplitter.Panel1.ResumeLayout(false);
            uiContentSplitter.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)uiContentSplitter).EndInit();
            uiContentSplitter.ResumeLayout(false);
            uiRightSplitter.Panel1.ResumeLayout(false);
            uiRightSplitter.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)uiRightSplitter).EndInit();
            uiRightSplitter.ResumeLayout(false);
            uiMetadataPanel.ResumeLayout(false);
            uiMetadataPanel.PerformLayout();
            uiLogPanel.ResumeLayout(false);
            uiLogPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel uiMainLayout;
        private Panel uiTopPanel;
        private Button uiGenerateButton;
        private Button uiExecuteButton;
        private Button uiCancelButton;
        private Button uiRefreshButton;
        private Label uiStatsLabel;
        private SplitContainer uiContentSplitter;
        private TreeView uiTreeView;
        private SplitContainer uiRightSplitter;
        private Panel uiMetadataPanel;
        private Label uiMetadataLabel;
        private Panel uiLogPanel;
        private Label uiLogLabel;
        private TextBox uiLogOutputTextBox;
    }
}
