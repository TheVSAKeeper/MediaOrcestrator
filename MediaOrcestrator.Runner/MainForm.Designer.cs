namespace MediaOrcestrator.Runner
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            uiMediaSourcePanel = new Panel();
            SuspendLayout();
            // 
            // uiMediaSourcePanel
            // 
            uiMediaSourcePanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            uiMediaSourcePanel.BackColor = SystemColors.ControlDark;
            uiMediaSourcePanel.Location = new Point(12, 12);
            uiMediaSourcePanel.Name = "uiMediaSourcePanel";
            uiMediaSourcePanel.Size = new Size(349, 432);
            uiMediaSourcePanel.TabIndex = 0;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 456);
            Controls.Add(uiMediaSourcePanel);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "MainForm";
            Text = "Медиа оркестратор";
            Load += MainForm_Load;
            ResumeLayout(false);
        }

        #endregion

        private Panel uiMediaSourcePanel;
    }
}
