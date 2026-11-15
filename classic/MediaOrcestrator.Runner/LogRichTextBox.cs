namespace MediaOrcestrator.Runner;

internal sealed class LogRichTextBox : RichTextBox
{
    private const int EM_STREAMIN = 0x0449;

    private bool _suppressSelectionChanged;

    protected override void OnSelectionChanged(EventArgs e)
    {
        if (_suppressSelectionChanged)
        {
            return;
        }

        base.OnSelectionChanged(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg != EM_STREAMIN)
        {
            base.WndProc(ref m);
            return;
        }

        var savedStart = SelectionStart;
        var savedLength = SelectionLength;
        var hadSelection = savedLength > 0;

        _suppressSelectionChanged = true;
        try
        {
            base.WndProc(ref m);
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        if (!hadSelection)
        {
            return;
        }

        var newLength = TextLength;
        if (savedStart >= newLength)
        {
            return;
        }

        SelectionStart = savedStart;
        SelectionLength = Math.Min(savedLength, newLength - savedStart);
    }
}
