using MediaOrcestrator.Domain;

namespace MediaOrcestrator.Runner;

public sealed partial class ActionUserControl : UserControl
{
    private static readonly Color CanceledBackColor = Color.Gainsboro;
    private static readonly Color CanceledForeColor = Color.DimGray;

    private readonly Color _defaultBackColor;
    private readonly Color _defaultNameForeColor;
    private readonly Color _defaultStatusForeColor;

    private ActionHolder.RunningAction? _action;
    private bool _isCanceled;

    public ActionUserControl()
    {
        InitializeComponent();

        _defaultBackColor = BackColor;
        _defaultNameForeColor = uiNameLabel.ForeColor;
        _defaultStatusForeColor = uiStatusLabel.ForeColor;

        Disposed += OnDisposed;
    }

    public void SetAction(ActionHolder.RunningAction action)
    {
        if (_action != null)
        {
            _action.Changed -= OnActionChanged;
        }

        _action = action;
        _action.Changed += OnActionChanged;
        UpdateStatus();
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
        if (_action == null)
        {
            return;
        }

        _action.Changed -= OnActionChanged;
        _action = null;
    }

    private void uiCancelButton_Click(object sender, EventArgs e)
    {
        if (_action == null)
        {
            return;
        }

        _isCanceled = true;
        uiCancelButton.Enabled = false;
        UpdateStatus();
        _action.Cancel();
    }

    private void UpdateStatus()
    {
        if (_action == null)
        {
            return;
        }

        uiNameLabel.Text = _action.Name;
        uiStatusLabel.Text = _action.Status;

        if (_isCanceled)
        {
            BackColor = CanceledBackColor;
            uiNameLabel.ForeColor = CanceledForeColor;
            uiStatusLabel.ForeColor = CanceledForeColor;
            uiProgressBar.Visible = false;
            uiProgressLabel.Visible = false;
            uiCancelButton.Visible = false;
            return;
        }

        BackColor = _defaultBackColor;
        uiNameLabel.ForeColor = _defaultNameForeColor;
        uiStatusLabel.ForeColor = _defaultStatusForeColor;

        var progressMax = _action.ProgressMax;
        if (progressMax > 0)
        {
            var progressValue = Math.Clamp(_action.ProgressValue, 0, progressMax);
            uiProgressBar.Visible = true;
            uiProgressBar.Maximum = progressMax;
            uiProgressBar.Value = progressValue;
            uiProgressLabel.Visible = true;
            uiProgressLabel.Text = $"{progressValue} / {progressMax}";
        }
        else
        {
            uiProgressBar.Visible = false;
            uiProgressLabel.Visible = false;
        }
    }
}
