using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class MainWindow : Window
{
    private static readonly WindowPlacementKeys WindowKeys = new(
        "wpf.window.left",
        "wpf.window.top",
        "wpf.window.width",
        "wpf.window.height",
        "wpf.window.maximized");

    private readonly ShellViewModel _viewModel;
    private readonly ISettingsStore _settings;

    public MainWindow(ShellViewModel viewModel, ISettingsStore settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;

        WindowChromeTheming.Attach(this);

        WindowPlacement.Restore(this, _settings, WindowKeys);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_viewModel.RequestClose())
        {
            e.Cancel = true;
            return;
        }

        WindowPlacement.Save(this, _settings, WindowKeys);

        base.OnClosing(e);
    }
}
