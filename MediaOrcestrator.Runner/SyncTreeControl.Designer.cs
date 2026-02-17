namespace MediaOrcestrator.Runner
{
    partial class SyncTreeControl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            uiTreeView = new TreeView();
            uiExecuteButton = new Button();
            SuspendLayout();
            // 
            // uiTreeView
            // 
            uiTreeView.Dock = DockStyle.Fill;
            uiTreeView.Location = new Point(0, 0);
            uiTreeView.Name = "uiTreeView";
            uiTreeView.Size = new Size(796, 606);
            uiTreeView.TabIndex = 0;
            // 
            // uiExecuteButton
            // 
            uiExecuteButton.Dock = DockStyle.Bottom;
            uiExecuteButton.Location = new Point(0, 606);
            uiExecuteButton.Name = "uiExecuteButton";
            uiExecuteButton.Size = new Size(796, 40);
            uiExecuteButton.TabIndex = 1;
            uiExecuteButton.Text = "Выполнить выбранное";
            uiExecuteButton.UseVisualStyleBackColor = true;
            uiExecuteButton.Click += uiExecuteButton_Click;
            // 
            // SyncTreeControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(uiTreeView);
            Controls.Add(uiExecuteButton);
            Name = "SyncTreeControl";
            Size = new Size(796, 646);
            ResumeLayout(false);
        }

        #endregion

        private TreeView uiTreeView;
        private Button uiExecuteButton;
    }
}
