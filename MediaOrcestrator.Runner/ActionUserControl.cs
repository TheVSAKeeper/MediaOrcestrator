using MediaOrcestrator.Domain;

namespace MediaOrcestrator.Runner;

public partial class ActionUserControl : UserControl
{
    private ActionHolder.RunningAction? _act;
    private bool _isCanceled;

    public ActionUserControl()
    {
        InitializeComponent();
        Disposed += OnDisposed;
    }

    public void SetAction(ActionHolder.RunningAction act)
    {
        if (_act != null)
        {
            _act.Changed -= OnActionChanged;
        }

        _act = act;
        _act.Changed += OnActionChanged;
        UpdateStatus();
    }

    public void UpdateStatus()
    {
        if (_act == null)
        {
            return;
        }

        label1.Text = _act.Name + " " + _act.Status;
        if (_isCanceled)
        {
            BackColor = Color.DarkGray;
            button1.Visible = false;
            return;
        }

        if (_act.ProgressMax > 0)
        {
            progressBar1.Visible = true;
            progressBar1.Maximum = _act.ProgressMax;
            progressBar1.Value = Math.Clamp(_act.ProgressValue, 0, _act.ProgressMax);
            label2.Visible = true;
            label2.Text = _act.ProgressValue + " / " + _act.ProgressMax;
        }
        else
        {
            progressBar1.Visible = false;
            label2.Visible = false;
        }
    }

    private void OnActionChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(UpdateStatus);
            return;
        }

        UpdateStatus();
    }

    private void OnDisposed(object? sender, EventArgs e)
    {
        if (_act == null)
        {
            return;
        }

        _act.Changed -= OnActionChanged;
        _act = null;
    }

    private void button1_Click(object sender, EventArgs e)
    {
        if (_act == null)
        {
            return;
        }

        _act.Status = "Отменено";
        _act.ProgressMax = 0;
        _isCanceled = true;
        UpdateStatus();
        _act.Cancel();
    }
}
