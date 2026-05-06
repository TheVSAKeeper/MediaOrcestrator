namespace MediaOrcestrator.Runner;

public sealed partial class SplashForm : Form
{
    public SplashForm()
    {
        InitializeComponent();
    }

    public void SetVersion(string version)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(SetVersion), version);
            return;
        }

        uiVersionLabel.Text = $"Версия {version}";
    }

    public void SetStatus(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(SetStatus), message);
            return;
        }

        uiStatusLabel.Text = message;
    }

    public void SetMaxSteps(int total)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<int>(SetMaxSteps), total);
            return;
        }

        uiProgressBar.Maximum = Math.Max(1, total);
        uiProgressBar.Value = 0;
    }

    public void Step(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Step), message);
            return;
        }

        uiStatusLabel.Text = message;

        if (uiProgressBar.Value < uiProgressBar.Maximum)
        {
            uiProgressBar.Value += 1;
        }
    }
}
