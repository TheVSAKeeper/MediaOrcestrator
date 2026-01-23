using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Runner;

public partial class MediaSourceControl : UserControl
{
    private Orcestrator _orcestrator;
    private MySource _source;

    public MediaSourceControl(Orcestrator orcestrator)
    {
        InitializeComponent();
        _orcestrator = orcestrator;
    }

    public void SetMediaSource(MySource source)
    {
        var sources = _orcestrator.GetSources();

        _source = source;
        // todo ключа пока нет
        label1.Text = sources.First(x => x.Value.Name == source.TypeId).Value.Name;
    }

    private async void button1_Click(object sender, EventArgs e)
    {
        await _orcestrator.Sync();
    }
}
