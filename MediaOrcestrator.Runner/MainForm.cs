using MediaOrcestrator.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace MediaOrcestrator.Runner;

public partial class MainForm : Form
{
    private readonly Orcestrator _orcestrator;
    private readonly IServiceProvider _serviceProvider;

    public MainForm(Orcestrator orcestrator, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _orcestrator = orcestrator;
        _serviceProvider = serviceProvider;
    }

    private void MainForm_Load(object sender, EventArgs e)
    {
        _orcestrator.Init();
        DrawSources();
    }

    private void DrawSources()
    {
        uiMediaSourcePanel.Controls.Clear();

        var shift = 10;
        foreach (var source in _orcestrator.GetSources())
        {
            // TODO: Сомнительно
            var control = _serviceProvider.GetRequiredService<MediaSourceControl>();
            control.SetMediaSource(source.Value);
            control.Width = uiMediaSourcePanel.Width - 20;
            control.Height = 80;
            control.Left = 10;
            control.Top = shift;
            shift += 100;
            uiMediaSourcePanel.Controls.Add(control);
        }
    }
}
