namespace MediaOrcestrator.Runner;

partial class CommentsHtmlControl
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchDebounce?.Dispose();
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Component Designer generated code

    private Panel uiFiltersPanel;
    private Label uiSourceLabel;
    private ComboBox uiSourceComboBox;
    private Label uiSearchLabel;
    private TextBox uiSearchTextBox;
    private Label uiLimitLabel;
    private NumericUpDown uiLimitNumeric;
    private Button uiRefreshButton;
    private Button uiForceFetchAllButton;
    private Label uiMediaSearchLabel;
    private TextBox uiMediaSearchTextBox;
    private Label uiMediaSortLabel;
    private ComboBox uiMediaSortComboBox;
    private Button uiMediaSortInvertButton;
    private Label uiCommentSortLabel;
    private ComboBox uiCommentSortComboBox;
    private Button uiCommentSortInvertButton;
    private Label uiCountLabel;
    private CheckBox uiFetchSinceCheckBox;
    private NumericUpDown uiFetchSinceDaysNumeric;
    private Label uiFetchSinceDaysLabel;
    private CheckBox uiFetchOnlyRecentCheckBox;
    private NumericUpDown uiFetchOnlyRecentNumeric;
    private Label uiFetchOnlyRecentLabel;
    private CommentsBrowserView uiBrowserView;
    private StatusStrip uiStatusStrip;
    private ToolStripStatusLabel uiStatusLabel;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        uiFiltersPanel = new Panel();
        uiSourceLabel = new Label();
        uiSourceComboBox = new ComboBox();
        uiSearchLabel = new Label();
        uiSearchTextBox = new TextBox();
        uiLimitLabel = new Label();
        uiLimitNumeric = new NumericUpDown();
        uiRefreshButton = new Button();
        uiForceFetchAllButton = new Button();
        uiMediaSearchLabel = new Label();
        uiMediaSearchTextBox = new TextBox();
        uiMediaSortLabel = new Label();
        uiMediaSortComboBox = new ComboBox();
        uiMediaSortInvertButton = new Button();
        uiCommentSortLabel = new Label();
        uiCommentSortComboBox = new ComboBox();
        uiCommentSortInvertButton = new Button();
        uiCountLabel = new Label();
        uiFetchSinceCheckBox = new CheckBox();
        uiFetchSinceDaysNumeric = new NumericUpDown();
        uiFetchSinceDaysLabel = new Label();
        uiFetchOnlyRecentCheckBox = new CheckBox();
        uiFetchOnlyRecentNumeric = new NumericUpDown();
        uiFetchOnlyRecentLabel = new Label();
        uiBrowserView = new CommentsBrowserView();
        uiStatusStrip = new StatusStrip();
        uiStatusLabel = new ToolStripStatusLabel();
        uiFiltersPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)uiLimitNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)uiFetchSinceDaysNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)uiFetchOnlyRecentNumeric).BeginInit();
        uiStatusStrip.SuspendLayout();
        SuspendLayout();
        //
        // uiFiltersPanel
        //
        uiFiltersPanel.Controls.Add(uiFetchOnlyRecentLabel);
        uiFiltersPanel.Controls.Add(uiFetchOnlyRecentNumeric);
        uiFiltersPanel.Controls.Add(uiFetchOnlyRecentCheckBox);
        uiFiltersPanel.Controls.Add(uiFetchSinceDaysLabel);
        uiFiltersPanel.Controls.Add(uiFetchSinceDaysNumeric);
        uiFiltersPanel.Controls.Add(uiFetchSinceCheckBox);
        uiFiltersPanel.Controls.Add(uiCountLabel);
        uiFiltersPanel.Controls.Add(uiCommentSortInvertButton);
        uiFiltersPanel.Controls.Add(uiCommentSortComboBox);
        uiFiltersPanel.Controls.Add(uiCommentSortLabel);
        uiFiltersPanel.Controls.Add(uiMediaSortInvertButton);
        uiFiltersPanel.Controls.Add(uiMediaSortComboBox);
        uiFiltersPanel.Controls.Add(uiMediaSortLabel);
        uiFiltersPanel.Controls.Add(uiMediaSearchTextBox);
        uiFiltersPanel.Controls.Add(uiMediaSearchLabel);
        uiFiltersPanel.Controls.Add(uiForceFetchAllButton);
        uiFiltersPanel.Controls.Add(uiRefreshButton);
        uiFiltersPanel.Controls.Add(uiLimitNumeric);
        uiFiltersPanel.Controls.Add(uiLimitLabel);
        uiFiltersPanel.Controls.Add(uiSearchTextBox);
        uiFiltersPanel.Controls.Add(uiSearchLabel);
        uiFiltersPanel.Controls.Add(uiSourceComboBox);
        uiFiltersPanel.Controls.Add(uiSourceLabel);
        uiFiltersPanel.Dock = DockStyle.Top;
        uiFiltersPanel.Location = new Point(0, 0);
        uiFiltersPanel.Name = "uiFiltersPanel";
        uiFiltersPanel.Padding = new Padding(8);
        uiFiltersPanel.Size = new Size(1100, 110);
        uiFiltersPanel.TabIndex = 0;
        //
        // uiSourceLabel
        //
        uiSourceLabel.AutoSize = true;
        uiSourceLabel.Location = new Point(8, 14);
        uiSourceLabel.Name = "uiSourceLabel";
        uiSourceLabel.Size = new Size(60, 15);
        uiSourceLabel.TabIndex = 0;
        uiSourceLabel.Text = "Источник:";
        //
        // uiSourceComboBox
        //
        uiSourceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        uiSourceComboBox.Location = new Point(80, 11);
        uiSourceComboBox.Name = "uiSourceComboBox";
        uiSourceComboBox.Size = new Size(220, 23);
        uiSourceComboBox.TabIndex = 1;
        uiSourceComboBox.SelectedIndexChanged += uiSourceComboBox_SelectedIndexChanged;
        //
        // uiSearchLabel
        //
        uiSearchLabel.AutoSize = true;
        uiSearchLabel.Location = new Point(316, 14);
        uiSearchLabel.Name = "uiSearchLabel";
        uiSearchLabel.Size = new Size(45, 15);
        uiSearchLabel.TabIndex = 2;
        uiSearchLabel.Text = "Поиск:";
        //
        // uiSearchTextBox
        //
        uiSearchTextBox.Location = new Point(370, 11);
        uiSearchTextBox.Name = "uiSearchTextBox";
        uiSearchTextBox.PlaceholderText = "текст или автор";
        uiSearchTextBox.Size = new Size(260, 23);
        uiSearchTextBox.TabIndex = 3;
        uiSearchTextBox.TextChanged += uiSearchTextBox_TextChanged;
        uiSearchTextBox.KeyDown += uiSearchTextBox_KeyDown;
        //
        // uiLimitLabel
        //
        uiLimitLabel.AutoSize = true;
        uiLimitLabel.Location = new Point(646, 14);
        uiLimitLabel.Name = "uiLimitLabel";
        uiLimitLabel.Size = new Size(50, 15);
        uiLimitLabel.TabIndex = 4;
        uiLimitLabel.Text = "Лимит:";
        //
        // uiLimitNumeric
        //
        uiLimitNumeric.Increment = 100;
        uiLimitNumeric.Location = new Point(700, 11);
        uiLimitNumeric.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
        uiLimitNumeric.Minimum = new decimal(new int[] { 50, 0, 0, 0 });
        uiLimitNumeric.Name = "uiLimitNumeric";
        uiLimitNumeric.Size = new Size(80, 23);
        uiLimitNumeric.TabIndex = 5;
        uiLimitNumeric.Value = new decimal(new int[] { 1000, 0, 0, 0 });
        //
        // uiRefreshButton
        //
        uiRefreshButton.Location = new Point(792, 10);
        uiRefreshButton.Name = "uiRefreshButton";
        uiRefreshButton.Size = new Size(110, 25);
        uiRefreshButton.TabIndex = 6;
        uiRefreshButton.Text = "Обновить";
        uiRefreshButton.UseVisualStyleBackColor = true;
        uiRefreshButton.Click += uiRefreshButton_Click;
        //
        // uiForceFetchAllButton
        //
        uiForceFetchAllButton.Enabled = false;
        uiForceFetchAllButton.Location = new Point(908, 10);
        uiForceFetchAllButton.Name = "uiForceFetchAllButton";
        uiForceFetchAllButton.Size = new Size(190, 25);
        uiForceFetchAllButton.TabIndex = 7;
        uiForceFetchAllButton.Text = "Загрузить все из источника";
        uiForceFetchAllButton.UseVisualStyleBackColor = true;
        uiForceFetchAllButton.Click += uiForceFetchAllButton_Click;
        //
        // uiMediaSearchLabel
        //
        uiMediaSearchLabel.AutoSize = true;
        uiMediaSearchLabel.Location = new Point(8, 47);
        uiMediaSearchLabel.Name = "uiMediaSearchLabel";
        uiMediaSearchLabel.Size = new Size(45, 15);
        uiMediaSearchLabel.TabIndex = 8;
        uiMediaSearchLabel.Text = "Медиа:";
        //
        // uiMediaSearchTextBox
        //
        uiMediaSearchTextBox.Location = new Point(60, 44);
        uiMediaSearchTextBox.Name = "uiMediaSearchTextBox";
        uiMediaSearchTextBox.PlaceholderText = "название медиа";
        uiMediaSearchTextBox.Size = new Size(180, 23);
        uiMediaSearchTextBox.TabIndex = 9;
        uiMediaSearchTextBox.TextChanged += uiMediaSearchTextBox_TextChanged;
        uiMediaSearchTextBox.KeyDown += uiSearchTextBox_KeyDown;
        //
        // uiMediaSortLabel
        //
        uiMediaSortLabel.AutoSize = true;
        uiMediaSortLabel.Location = new Point(250, 47);
        uiMediaSortLabel.Name = "uiMediaSortLabel";
        uiMediaSortLabel.Size = new Size(75, 15);
        uiMediaSortLabel.TabIndex = 10;
        uiMediaSortLabel.Text = "Сорт. медиа:";
        //
        // uiMediaSortComboBox
        //
        uiMediaSortComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        uiMediaSortComboBox.Location = new Point(335, 44);
        uiMediaSortComboBox.Name = "uiMediaSortComboBox";
        uiMediaSortComboBox.Size = new Size(150, 23);
        uiMediaSortComboBox.TabIndex = 11;
        uiMediaSortComboBox.SelectedIndexChanged += uiSortChanged;
        //
        // uiMediaSortInvertButton
        //
        uiMediaSortInvertButton.Location = new Point(488, 43);
        uiMediaSortInvertButton.Name = "uiMediaSortInvertButton";
        uiMediaSortInvertButton.Size = new Size(28, 25);
        uiMediaSortInvertButton.TabIndex = 12;
        uiMediaSortInvertButton.Text = "↓";
        uiMediaSortInvertButton.UseVisualStyleBackColor = true;
        uiMediaSortInvertButton.Click += uiMediaSortInvertButton_Click;
        //
        // uiCommentSortLabel
        //
        uiCommentSortLabel.AutoSize = true;
        uiCommentSortLabel.Location = new Point(525, 47);
        uiCommentSortLabel.Name = "uiCommentSortLabel";
        uiCommentSortLabel.Size = new Size(80, 15);
        uiCommentSortLabel.TabIndex = 13;
        uiCommentSortLabel.Text = "Сорт. комм.:";
        //
        // uiCommentSortComboBox
        //
        uiCommentSortComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        uiCommentSortComboBox.Location = new Point(610, 44);
        uiCommentSortComboBox.Name = "uiCommentSortComboBox";
        uiCommentSortComboBox.Size = new Size(180, 23);
        uiCommentSortComboBox.TabIndex = 14;
        uiCommentSortComboBox.SelectedIndexChanged += uiSortChanged;
        //
        // uiCommentSortInvertButton
        //
        uiCommentSortInvertButton.Location = new Point(793, 43);
        uiCommentSortInvertButton.Name = "uiCommentSortInvertButton";
        uiCommentSortInvertButton.Size = new Size(28, 25);
        uiCommentSortInvertButton.TabIndex = 15;
        uiCommentSortInvertButton.Text = "↓";
        uiCommentSortInvertButton.UseVisualStyleBackColor = true;
        uiCommentSortInvertButton.Click += uiCommentSortInvertButton_Click;
        //
        // uiCountLabel
        //
        uiCountLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        uiCountLabel.AutoSize = true;
        uiCountLabel.ForeColor = Color.FromArgb(100, 100, 100);
        uiCountLabel.Location = new Point(1000, 47);
        uiCountLabel.Name = "uiCountLabel";
        uiCountLabel.Size = new Size(60, 15);
        uiCountLabel.TabIndex = 16;
        uiCountLabel.Text = "Найдено: 0";
        //
        // uiFetchSinceCheckBox
        //
        uiFetchSinceCheckBox.AutoSize = true;
        uiFetchSinceCheckBox.Location = new Point(8, 80);
        uiFetchSinceCheckBox.Name = "uiFetchSinceCheckBox";
        uiFetchSinceCheckBox.Size = new Size(95, 19);
        uiFetchSinceCheckBox.TabIndex = 17;
        uiFetchSinceCheckBox.Text = "Не старше";
        uiFetchSinceCheckBox.UseVisualStyleBackColor = true;
        uiFetchSinceCheckBox.CheckedChanged += uiFetchSinceCheckBox_CheckedChanged;
        //
        // uiFetchSinceDaysNumeric
        //
        uiFetchSinceDaysNumeric.Enabled = false;
        uiFetchSinceDaysNumeric.Location = new Point(110, 78);
        uiFetchSinceDaysNumeric.Maximum = new decimal(new int[] { 3650, 0, 0, 0 });
        uiFetchSinceDaysNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        uiFetchSinceDaysNumeric.Name = "uiFetchSinceDaysNumeric";
        uiFetchSinceDaysNumeric.Size = new Size(70, 23);
        uiFetchSinceDaysNumeric.TabIndex = 18;
        uiFetchSinceDaysNumeric.Value = new decimal(new int[] { 30, 0, 0, 0 });
        //
        // uiFetchSinceDaysLabel
        //
        uiFetchSinceDaysLabel.AutoSize = true;
        uiFetchSinceDaysLabel.Location = new Point(184, 81);
        uiFetchSinceDaysLabel.Name = "uiFetchSinceDaysLabel";
        uiFetchSinceDaysLabel.Size = new Size(28, 15);
        uiFetchSinceDaysLabel.TabIndex = 19;
        uiFetchSinceDaysLabel.Text = "дн.";
        //
        // uiFetchOnlyRecentCheckBox
        //
        uiFetchOnlyRecentCheckBox.AutoSize = true;
        uiFetchOnlyRecentCheckBox.Location = new Point(240, 80);
        uiFetchOnlyRecentCheckBox.Name = "uiFetchOnlyRecentCheckBox";
        uiFetchOnlyRecentCheckBox.Size = new Size(140, 19);
        uiFetchOnlyRecentCheckBox.TabIndex = 20;
        uiFetchOnlyRecentCheckBox.Text = "Только последние";
        uiFetchOnlyRecentCheckBox.UseVisualStyleBackColor = true;
        uiFetchOnlyRecentCheckBox.CheckedChanged += uiFetchOnlyRecentCheckBox_CheckedChanged;
        //
        // uiFetchOnlyRecentNumeric
        //
        uiFetchOnlyRecentNumeric.Enabled = false;
        uiFetchOnlyRecentNumeric.Location = new Point(385, 78);
        uiFetchOnlyRecentNumeric.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
        uiFetchOnlyRecentNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        uiFetchOnlyRecentNumeric.Name = "uiFetchOnlyRecentNumeric";
        uiFetchOnlyRecentNumeric.Size = new Size(80, 23);
        uiFetchOnlyRecentNumeric.TabIndex = 21;
        uiFetchOnlyRecentNumeric.Value = new decimal(new int[] { 50, 0, 0, 0 });
        //
        // uiFetchOnlyRecentLabel
        //
        uiFetchOnlyRecentLabel.AutoSize = true;
        uiFetchOnlyRecentLabel.Location = new Point(469, 81);
        uiFetchOnlyRecentLabel.Name = "uiFetchOnlyRecentLabel";
        uiFetchOnlyRecentLabel.Size = new Size(45, 15);
        uiFetchOnlyRecentLabel.TabIndex = 22;
        uiFetchOnlyRecentLabel.Text = "медиа";
        //
        // uiBrowserView
        //
        uiBrowserView.Dock = DockStyle.Fill;
        uiBrowserView.Location = new Point(0, 110);
        uiBrowserView.Name = "uiBrowserView";
        uiBrowserView.Size = new Size(1100, 518);
        uiBrowserView.TabIndex = 1;
        //
        // uiStatusStrip
        //
        uiStatusStrip.Items.AddRange(new ToolStripItem[] { uiStatusLabel });
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
        ((System.ComponentModel.ISupportInitialize)uiLimitNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)uiFetchSinceDaysNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)uiFetchOnlyRecentNumeric).EndInit();
        uiStatusStrip.ResumeLayout(false);
        uiStatusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion
}
