#nullable enable

namespace MediaOrcestrator.Runner
{
    partial class FilterToolStripControl
    {
        private System.ComponentModel.IContainer? components = null;
        private System.Threading.Timer? _searchDebounceTimer;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();

                if (_searchDebounceTimer != null)
                {
                    _searchDebounceTimer.Dispose();
                    _searchDebounceTimer = null;
                }
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            uiToolStrip = new ToolStrip();
            uiSearchLabel = new ToolStripLabel();
            uiSearchToolStripTextBox = new ToolStripTextBox();
            uiClearSearchButton = new ToolStripButton();
            uiStatusLabel = new ToolStripLabel();
            uiStatusFilterComboBox = new ToolStripComboBox();
            uiRelationsDropDownButton = new ToolStripDropDownButton();
            uiMetadataDropDownButton = new ToolStripDropDownButton();
            uiToolStrip.SuspendLayout();
            SuspendLayout();
            // 
            // uiToolStrip
            // 
            uiToolStrip.Items.AddRange(new ToolStripItem[]
            {
                uiSearchLabel,
                uiSearchToolStripTextBox,
                uiClearSearchButton,
                uiStatusLabel,
                uiStatusFilterComboBox,
                uiRelationsDropDownButton,
                uiMetadataDropDownButton,
            });
            uiToolStrip.Dock = DockStyle.Fill;
            uiToolStrip.Name = "uiToolStrip";
            uiToolStrip.Size = new Size(595, 25);
            uiToolStrip.TabIndex = 0;
            // 
            // uiSearchLabel
            // 
            uiSearchLabel.Name = "uiSearchLabel";
            uiSearchLabel.Size = new Size(45, 22);
            uiSearchLabel.Text = "Поиск:";
            // 
            // uiSearchToolStripTextBox
            // 
            uiSearchToolStripTextBox.Name = "uiSearchToolStripTextBox";
            uiSearchToolStripTextBox.Size = new Size(200, 25);
            uiSearchToolStripTextBox.TextChanged += uiSearchToolStripTextBox_TextChanged;
            // 
            // uiClearSearchButton
            // 
            uiClearSearchButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            uiClearSearchButton.Name = "uiClearSearchButton";
            uiClearSearchButton.Size = new Size(65, 22);
            uiClearSearchButton.Text = "Очистить";
            uiClearSearchButton.Click += uiClearSearchButton_Click;
            // 
            // uiStatusLabel
            // 
            uiStatusLabel.Name = "uiStatusLabel";
            uiStatusLabel.Size = new Size(48, 22);
            uiStatusLabel.Text = "Статус:";
            uiStatusLabel.Margin = new Padding(10, 1, 0, 2);
            // 
            // uiStatusFilterComboBox
            // 
            uiStatusFilterComboBox.Name = "uiStatusFilterComboBox";
            uiStatusFilterComboBox.Size = new Size(100, 25);
            uiStatusFilterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            uiStatusFilterComboBox.SelectedIndexChanged += uiStatusFilterComboBox_SelectedIndexChanged;
            // 
            // uiRelationsDropDownButton
            // 
            uiRelationsDropDownButton.Name = "uiRelationsDropDownButton";
            uiRelationsDropDownButton.Size = new Size(130, 22);
            uiRelationsDropDownButton.Text = "Фильтр по связям";
            uiRelationsDropDownButton.Margin = new Padding(10, 1, 0, 2);
            // 
            // uiMetadataDropDownButton
            // 
            uiMetadataDropDownButton.Name = "uiMetadataDropDownButton";
            uiMetadataDropDownButton.Size = new Size(130, 22);
            uiMetadataDropDownButton.Text = "Колонки метаданных";
            uiMetadataDropDownButton.Margin = new Padding(10, 1, 0, 2);
            // 
            // FilterToolStripControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(uiToolStrip);
            Name = "FilterToolStripControl";
            Size = new Size(595, 25);
            uiToolStrip.ResumeLayout(false);
            uiToolStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        private ToolStrip uiToolStrip;
        private ToolStripLabel uiSearchLabel;
        private ToolStripTextBox uiSearchToolStripTextBox;
        private ToolStripButton uiClearSearchButton;
        private ToolStripLabel uiStatusLabel;
        private ToolStripComboBox uiStatusFilterComboBox;
        private ToolStripDropDownButton uiRelationsDropDownButton;
        private ToolStripDropDownButton uiMetadataDropDownButton;
    }
}
