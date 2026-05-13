namespace MediaOrcestrator.Runner;

partial class CommentsHtmlControl
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _applyFiltersCts?.Cancel();
            _applyFiltersCts?.Dispose();
            _searchDebounce?.Dispose();
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Component Designer generated code

    private TableLayoutPanel uiFiltersPanel;
    private GroupBox uiFetchGroup;
    private TableLayoutPanel uiFetchLayout;
    private Label uiSourceLabel;
    private ComboBox uiSourceComboBox;
    private Button uiForceFetchAllButton;
    private GroupBox uiViewGroup;
    private TableLayoutPanel uiViewLayout;
    private Label uiSearchLabel;
    private TextBox uiSearchTextBox;
    private Label uiLimitLabel;
    private NumericUpDown uiLimitNumeric;
    private CommentsBrowserView uiBrowserView;
    private StatusStrip uiStatusStrip;
    private ToolStripStatusLabel uiStatusLabel;
    private ToolStripProgressBar uiFetchProgressBar;
    private ToolStripStatusLabel uiFetchCounterLabel;
    private ToolTip uiToolTip;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        uiFiltersPanel = new TableLayoutPanel();
        uiFetchGroup = new GroupBox();
        uiFetchLayout = new TableLayoutPanel();
        uiSourceLabel = new Label();
        uiSourceComboBox = new ComboBox();
        uiForceFetchAllButton = new Button();
        uiViewGroup = new GroupBox();
        uiViewLayout = new TableLayoutPanel();
        uiSearchLabel = new Label();
        uiSearchTextBox = new TextBox();
        uiLimitLabel = new Label();
        uiLimitNumeric = new NumericUpDown();
        uiBrowserView = new CommentsBrowserView();
        uiStatusStrip = new StatusStrip();
        uiStatusLabel = new ToolStripStatusLabel();
        uiFetchProgressBar = new ToolStripProgressBar();
        uiFetchCounterLabel = new ToolStripStatusLabel();
        uiToolTip = new ToolTip(components);
        uiFiltersPanel.SuspendLayout();
        uiFetchGroup.SuspendLayout();
        uiFetchLayout.SuspendLayout();
        uiViewGroup.SuspendLayout();
        uiViewLayout.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)uiLimitNumeric).BeginInit();
        uiStatusStrip.SuspendLayout();
        SuspendLayout();
        //
        // uiFiltersPanel
        //
        uiFiltersPanel.ColumnCount = 2;
        uiFiltersPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        uiFiltersPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        uiFiltersPanel.Controls.Add(uiFetchGroup, 0, 0);
        uiFiltersPanel.Controls.Add(uiViewGroup, 1, 0);
        uiFiltersPanel.Dock = DockStyle.Top;
        uiFiltersPanel.Location = new Point(0, 0);
        uiFiltersPanel.Name = "uiFiltersPanel";
        uiFiltersPanel.Padding = new Padding(4, 0, 4, 0);
        uiFiltersPanel.RowCount = 1;
        uiFiltersPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));
        uiFiltersPanel.Size = new Size(1100, 150);
        uiFiltersPanel.TabIndex = 0;
        //
        // uiFetchGroup
        //
        uiFetchGroup.Controls.Add(uiFetchLayout);
        uiFetchGroup.Dock = DockStyle.Fill;
        uiFetchGroup.Margin = new Padding(0, 0, 4, 0);
        uiFetchGroup.Name = "uiFetchGroup";
        uiFetchGroup.TabIndex = 0;
        uiFetchGroup.TabStop = false;
        uiFetchGroup.Text = "Загрузка комментариев";
        //
        // uiFetchLayout
        //
        uiFetchLayout.ColumnCount = 2;
        uiFetchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        uiFetchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        uiFetchLayout.Controls.Add(uiSourceLabel, 0, 0);
        uiFetchLayout.Controls.Add(uiSourceComboBox, 1, 0);
        uiFetchLayout.Controls.Add(uiForceFetchAllButton, 0, 1);
        uiFetchLayout.SetColumnSpan(uiForceFetchAllButton, 2);
        uiFetchLayout.Dock = DockStyle.Fill;
        uiFetchLayout.Name = "uiFetchLayout";
        uiFetchLayout.RowCount = 2;
        uiFetchLayout.RowStyles.Add(new RowStyle());
        uiFetchLayout.RowStyles.Add(new RowStyle());
        uiFetchLayout.TabIndex = 0;
        //
        // uiSourceLabel
        //
        uiSourceLabel.AutoSize = true;
        uiSourceLabel.Margin = new Padding(0, 7, 6, 0);
        uiSourceLabel.Name = "uiSourceLabel";
        uiSourceLabel.TabIndex = 0;
        uiSourceLabel.Text = "Источник:";
        //
        // uiSourceComboBox
        //
        uiSourceComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        uiSourceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        uiSourceComboBox.Margin = new Padding(0, 3, 0, 3);
        uiSourceComboBox.Name = "uiSourceComboBox";
        uiSourceComboBox.Size = new Size(200, 23);
        uiSourceComboBox.TabIndex = 1;
        uiSourceComboBox.SelectedIndexChanged += uiSourceComboBox_SelectedIndexChanged;
        //
        // uiForceFetchAllButton
        //
        uiForceFetchAllButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        uiForceFetchAllButton.Enabled = false;
        uiForceFetchAllButton.Margin = new Padding(0, 4, 0, 2);
        uiForceFetchAllButton.Name = "uiForceFetchAllButton";
        uiForceFetchAllButton.Size = new Size(190, 25);
        uiForceFetchAllButton.TabIndex = 2;
        uiForceFetchAllButton.Text = "Загрузить из источника...";
        uiForceFetchAllButton.UseVisualStyleBackColor = true;
        uiForceFetchAllButton.Click += uiForceFetchAllButton_Click;
        uiToolTip.SetToolTip(uiForceFetchAllButton,
            "Открыть параметры и загрузить комментарии для медиа выбранного источника."
            + Environment.NewLine
            + "Для каждого попавшего медиа всегда загружаются все его комментарии.");
        //
        // uiViewGroup
        //
        uiViewGroup.Controls.Add(uiViewLayout);
        uiViewGroup.Dock = DockStyle.Fill;
        uiViewGroup.Margin = new Padding(0);
        uiViewGroup.Name = "uiViewGroup";
        uiViewGroup.TabIndex = 1;
        uiViewGroup.TabStop = false;
        uiViewGroup.Text = "Просмотр";
        //
        // uiViewLayout
        //
        uiViewLayout.ColumnCount = 2;
        uiViewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        uiViewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        uiViewLayout.Controls.Add(uiSearchLabel, 0, 0);
        uiViewLayout.Controls.Add(uiSearchTextBox, 1, 0);
        uiViewLayout.Controls.Add(uiLimitLabel, 0, 1);
        uiViewLayout.Controls.Add(uiLimitNumeric, 1, 1);
        uiViewLayout.Dock = DockStyle.Fill;
        uiViewLayout.Name = "uiViewLayout";
        uiViewLayout.RowCount = 2;
        uiViewLayout.RowStyles.Add(new RowStyle());
        uiViewLayout.RowStyles.Add(new RowStyle());
        uiViewLayout.TabIndex = 0;
        //
        // uiSearchLabel
        //
        uiSearchLabel.AutoSize = true;
        uiSearchLabel.Margin = new Padding(0, 7, 6, 0);
        uiSearchLabel.Name = "uiSearchLabel";
        uiSearchLabel.TabIndex = 0;
        uiSearchLabel.Text = "Поиск:";
        //
        // uiSearchTextBox
        //
        uiSearchTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        uiSearchTextBox.Margin = new Padding(0, 3, 0, 3);
        uiSearchTextBox.Name = "uiSearchTextBox";
        uiSearchTextBox.PlaceholderText = "текст, автор или название медиа";
        uiSearchTextBox.Size = new Size(200, 23);
        uiSearchTextBox.TabIndex = 1;
        uiSearchTextBox.TextChanged += uiSearchTextBox_TextChanged;
        uiSearchTextBox.KeyDown += uiSearchTextBox_KeyDown;
        //
        // uiLimitLabel
        //
        uiLimitLabel.AutoSize = true;
        uiLimitLabel.Margin = new Padding(0, 7, 6, 0);
        uiLimitLabel.Name = "uiLimitLabel";
        uiLimitLabel.TabIndex = 2;
        uiLimitLabel.Text = "Показывать:";
        //
        // uiLimitNumeric
        //
        uiLimitNumeric.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        uiLimitNumeric.Increment = 100;
        uiLimitNumeric.Margin = new Padding(0, 3, 0, 3);
        uiLimitNumeric.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
        uiLimitNumeric.Minimum = new decimal(new int[] { 50, 0, 0, 0 });
        uiLimitNumeric.Name = "uiLimitNumeric";
        uiLimitNumeric.Size = new Size(80, 23);
        uiLimitNumeric.TabIndex = 3;
        uiLimitNumeric.Value = new decimal(new int[] { 1000, 0, 0, 0 });
        uiLimitNumeric.ValueChanged += uiFetchSettingsValueChanged;
        uiToolTip.SetToolTip(uiLimitNumeric,
            "Сколько комментариев показывать в списке."
            + Environment.NewLine
            + "Загрузка из источника всегда тянет все комментарии медиа целиком.");
        //
        // uiBrowserView
        //
        uiBrowserView.Dock = DockStyle.Fill;
        uiBrowserView.Location = new Point(0, 150);
        uiBrowserView.Name = "uiBrowserView";
        uiBrowserView.Size = new Size(1100, 508);
        uiBrowserView.TabIndex = 1;
        //
        // uiStatusStrip
        //
        uiStatusStrip.Items.AddRange(new ToolStripItem[] { uiStatusLabel, uiFetchCounterLabel, uiFetchProgressBar });
        uiStatusStrip.Location = new Point(0, 628);
        uiStatusStrip.Name = "uiStatusStrip";
        uiStatusStrip.Size = new Size(1100, 22);
        uiStatusStrip.TabIndex = 2;
        //
        // uiStatusLabel
        //
        uiStatusLabel.Name = "uiStatusLabel";
        uiStatusLabel.Size = new Size(1085, 17);
        uiStatusLabel.Spring = true;
        uiStatusLabel.Text = "Готов";
        uiStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // uiFetchProgressBar
        //
        uiFetchProgressBar.Alignment = ToolStripItemAlignment.Right;
        uiFetchProgressBar.Name = "uiFetchProgressBar";
        uiFetchProgressBar.Size = new Size(200, 16);
        uiFetchProgressBar.Visible = false;
        //
        // uiFetchCounterLabel
        //
        uiFetchCounterLabel.Alignment = ToolStripItemAlignment.Right;
        uiFetchCounterLabel.Name = "uiFetchCounterLabel";
        uiFetchCounterLabel.Padding = new Padding(6, 0, 6, 0);
        uiFetchCounterLabel.TextAlign = ContentAlignment.MiddleRight;
        uiFetchCounterLabel.Visible = false;
        //
        // CommentsHtmlControl
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(uiBrowserView);
        Controls.Add(uiStatusStrip);
        Controls.Add(uiFiltersPanel);
        Name = "CommentsHtmlControl";
        Size = new Size(1100, 650);
        uiFiltersPanel.ResumeLayout(false);
        uiFiltersPanel.PerformLayout();
        uiFetchGroup.ResumeLayout(false);
        uiFetchGroup.PerformLayout();
        uiFetchLayout.ResumeLayout(false);
        uiFetchLayout.PerformLayout();
        uiViewGroup.ResumeLayout(false);
        uiViewGroup.PerformLayout();
        uiViewLayout.ResumeLayout(false);
        uiViewLayout.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)uiLimitNumeric).EndInit();
        uiStatusStrip.ResumeLayout(false);
        uiStatusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion
}
