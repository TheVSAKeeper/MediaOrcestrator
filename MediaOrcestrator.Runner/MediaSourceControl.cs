using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Runner;

public partial class MediaSourceControl : UserControl
{
    private Orcestrator _orcestrator;
    private IMediaSource _source;

    public MediaSourceControl(Orcestrator orcestrator)
    {
        InitializeComponent();
        _orcestrator = orcestrator;
    }

    public void SetMediaSource(IMediaSource source)
    {
        _source = source;
        label1.Text = source.Name;
    }

    private async void button1_Click(object sender, EventArgs e)
    {
        await _orcestrator.Sync();
    }
}
