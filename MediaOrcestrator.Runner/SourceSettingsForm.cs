using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Runner;

public partial class SourceSettingsForm : Form
{
    private readonly Dictionary<string, TextBox> _textBoxes = new();
    private IEnumerable<SourceSettings> _settingsKeys = [];

    public SourceSettingsForm()
    {
        InitializeComponent();
    }

    public Dictionary<string, string>? Settings { get; private set; }

    public void SetSettings(IEnumerable<SourceSettings> settingsKeys)
    {
        _settingsKeys = settingsKeys;
    }

    private void SourceSettingsForm_Load(object sender, EventArgs e)
    {
        foreach (var setting in _settingsKeys)
        {
            var label = new Label { Text = setting.Title, AutoSize = true };
            var textBox = new TextBox { Width = 350 };
            _textBoxes.Add(setting.Key, textBox);

            panel1.Controls.Add(label);
            panel1.Controls.Add(textBox);
        }
    }

    private void button1_Click(object sender, EventArgs e)
    {
        Settings = new();
        if (string.IsNullOrEmpty(uiNameTextBox.Text))
        {
            MessageBox.Show("имя обязательно");
            return;
        }

        Settings.Add("_system_name", uiNameTextBox.Text);
        foreach (var (key, value) in _textBoxes)
        {
            Settings.Add(key, value.Text);
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
