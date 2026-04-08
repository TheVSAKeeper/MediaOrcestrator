namespace MediaOrcestrator.Runner;

public enum InputBrowseMode
{
    None = 0,
    Folder = 1,
    File = 2,
}

public partial class InputDialog : Form
{
    private readonly InputBrowseMode _browseMode;

    public InputDialog()
    {
        InitializeComponent();
    }

    public InputDialog(string prompt, string title = "Ввод данных", string defaultValue = "", InputBrowseMode browseMode = InputBrowseMode.None)
    {
        _browseMode = browseMode;

        InitializeComponent();
        Text = title;
        uiPromptLabel.Text = prompt;
        uiInputTextBox.Text = defaultValue;
        uiInputTextBox.Select();

        if (browseMode != InputBrowseMode.None)
        {
            AddBrowseButton();
        }
    }

    public string InputText { get; private set; }

    private void uiBrowseButton_Click(object? sender, EventArgs e)
    {
        if (_browseMode == InputBrowseMode.Folder)
        {
            using var dialog = new FolderBrowserDialog();

            if (!string.IsNullOrEmpty(uiInputTextBox.Text) && Directory.Exists(uiInputTextBox.Text))
            {
                dialog.SelectedPath = uiInputTextBox.Text;
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                uiInputTextBox.Text = dialog.SelectedPath;
            }
        }
        else if (_browseMode == InputBrowseMode.File)
        {
            using var dialog = new OpenFileDialog();

            if (!string.IsNullOrEmpty(uiInputTextBox.Text))
            {
                var dir = Path.GetDirectoryName(uiInputTextBox.Text);

                if (dir != null && Directory.Exists(dir))
                {
                    dialog.InitialDirectory = dir;
                }

                dialog.FileName = Path.GetFileName(uiInputTextBox.Text);
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                uiInputTextBox.Text = dialog.FileName;
            }
        }
    }

    private void uiOkButton_Click(object sender, EventArgs e)
    {
        InputText = uiInputTextBox.Text;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void uiCancelButton_Click(object sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void AddBrowseButton()
    {
        var uiBrowseButton = new Button
        {
            Text = "...",
            Size = new(30, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };

        uiBrowseButton.Location = new(uiInputTextBox.Right - uiBrowseButton.Width, uiInputTextBox.Top);
        uiInputTextBox.Width -= uiBrowseButton.Width + 4;
        uiBrowseButton.Click += uiBrowseButton_Click;
        Controls.Add(uiBrowseButton);
    }
}
