namespace MediaOrcestrator.Runner;

partial class InputDialog
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

    #region Windows Form Designer generated code
    private Label uiPromptLabel;
    private TextBox uiInputTextBox;
    private Button uiOkButton;
    private Button uiCancelButton;

    private void InitializeComponent()
    {
        uiPromptLabel = new Label();
        uiInputTextBox = new TextBox();
        uiOkButton = new Button();
        uiCancelButton = new Button();
        SuspendLayout();
        // 
        // uiPromptLabel
        // 
        uiPromptLabel.AutoSize = true;
        uiPromptLabel.Location = new Point(14, 17);
        uiPromptLabel.Margin = new Padding(4, 0, 4, 0);
        uiPromptLabel.Name = "uiPromptLabel";
        uiPromptLabel.Size = new Size(115, 15);
        uiPromptLabel.TabIndex = 0;
        uiPromptLabel.Text = "Тут текстовый текст";
        // 
        // uiInputTextBox
        // 
        uiInputTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        uiInputTextBox.Location = new Point(14, 46);
        uiInputTextBox.Margin = new Padding(4, 3, 4, 3);
        uiInputTextBox.Name = "uiInputTextBox";
        uiInputTextBox.Size = new Size(482, 23);
        uiInputTextBox.TabIndex = 1;
        // 
        // uiOkButton
        // 
        uiOkButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        uiOkButton.Location = new Point(314, 81);
        uiOkButton.Margin = new Padding(4, 3, 4, 3);
        uiOkButton.Name = "uiOkButton";
        uiOkButton.Size = new Size(88, 27);
        uiOkButton.TabIndex = 2;
        uiOkButton.Text = "OK";
        uiOkButton.Click += uiOkButton_Click;
        // 
        // uiCancelButton
        // 
        uiCancelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        uiCancelButton.Location = new Point(409, 81);
        uiCancelButton.Margin = new Padding(4, 3, 4, 3);
        uiCancelButton.Name = "uiCancelButton";
        uiCancelButton.Size = new Size(88, 27);
        uiCancelButton.TabIndex = 3;
        uiCancelButton.Text = "Отмена";
        uiCancelButton.Click += uiCancelButton_Click;
        // 
        // InputDialog
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(510, 121);
        Controls.Add(uiPromptLabel);
        Controls.Add(uiInputTextBox);
        Controls.Add(uiOkButton);
        Controls.Add(uiCancelButton);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Margin = new Padding(4, 3, 4, 3);
        Name = "InputDialog";
        StartPosition = FormStartPosition.CenterParent;
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion
}
