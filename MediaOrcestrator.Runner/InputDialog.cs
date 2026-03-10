namespace MediaOrcestrator.Runner;

public partial class InputDialog : Form
{
    public InputDialog()
    {
        InitializeComponent();
    }

    public InputDialog(string prompt, string title = "Ввод данных", string defaultValue = "")
    {
        InitializeComponent();
        Text = title;
        lblPrompt.Text = prompt;
        txtInput.Text = defaultValue;
        txtInput.Select();
    }

    public string InputText { get; private set; }

    private void btnOk_Click(object sender, EventArgs e)
    {
        InputText = txtInput.Text;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void btnCancel_Click(object sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }
}
