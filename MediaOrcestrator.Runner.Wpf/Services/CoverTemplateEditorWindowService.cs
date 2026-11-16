using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.Views.Dialogs;
using System.ComponentModel;
using System.Windows;

namespace MediaOrcestrator.Runner.Wpf.Services;

public sealed class CoverTemplateEditorWindowService : ICoverTemplateEditorWindowService
{
    private readonly CoverGenerator _generator;
    private readonly CoverTemplateStore _store;
    private readonly IDialogService _dialogs;
    private readonly IFilePicker _filePicker;

    private CoverTemplateEditorWindow? _window;
    private CoverTemplateEditorViewModel? _activeViewModel;
    private CoverProfilePickerViewModel? _activeCaller;
    private bool _appHooked;
    private bool _shuttingDown;

    public CoverTemplateEditorWindowService(
        CoverGenerator generator,
        CoverTemplateStore store,
        IDialogService dialogs,
        IFilePicker filePicker)
    {
        _generator = generator;
        _store = store;
        _dialogs = dialogs;
        _filePicker = filePicker;
    }

    public bool IsActiveCaller(CoverProfilePickerViewModel picker) => ReferenceEquals(_activeCaller, picker);

    public void Open(CoverTemplate? initial, string? profileName, string? sampleTitle, CoverProfilePickerViewModel? caller = null)
    {
        var app = Application.Current;

        if (app is null)
        {
            return;
        }

        HookApplicationLifetime(app);
        EnsureWindow();

        var owner = app.MainWindow is { IsLoaded: true } main ? main : null;

        if (!ReferenceEquals(_window!.Owner, owner))
        {
            _window.Owner = owner;
        }

        if (_window is { IsVisible: true }
            && _activeViewModel is { IsDirty: true }
            && !_dialogs.Confirm("Несохранённые изменения",
                "В редакторе шаблона есть несохранённые изменения. Закрыть текущий шаблон и открыть другой?"))
        {
            _window.Activate();
            return;
        }

        DetachActiveViewModel();

        var viewModel = new CoverTemplateEditorViewModel(
            _generator,
            _store,
            _dialogs,
            _filePicker,
            initial,
            profileName,
            sampleTitle);
        viewModel.CloseRequested += OnEditorCloseRequested;

        _activeViewModel = viewModel;
        _activeCaller = caller;
        _window.DataContext = viewModel;

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.SetCurrentValue(Window.WindowStateProperty, WindowState.Normal);
        }

        _window.Activate();
    }

    private void HookApplicationLifetime(Application app)
    {
        if (_appHooked)
        {
            return;
        }

        _appHooked = true;
        app.Exit += (_, _) => ShutdownWindow();

        if (app.MainWindow is { } main)
        {
            main.Closed += (_, _) => ShutdownWindow();
        }
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new CoverTemplateEditorWindow();
        WindowChromeTheming.Attach(_window);
        _window.Closing += OnWindowClosing;
        _window.Closed += OnWindowClosed;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_shuttingDown)
        {
            DetachActiveViewModel();
            _activeCaller = null;
            return;
        }

        if (_activeViewModel is { IsDirty: true }
            && !_dialogs.Confirm("Несохранённые изменения",
                "В шаблоне обложки есть несохранённые изменения. Закрыть окно без сохранения?"))
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        DetachActiveViewModel();
        _activeCaller = null;

        if (_window is not null)
        {
            _window.DataContext = null;
            _window.Hide();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        DetachActiveViewModel();
        _activeCaller = null;
        _window = null;
    }

    private void OnEditorCloseRequested(object? sender, EventArgs e)
    {
        _window?.Close();
    }

    private void DetachActiveViewModel()
    {
        if (_activeViewModel is null)
        {
            return;
        }

        _activeViewModel.CloseRequested -= OnEditorCloseRequested;
        _activeViewModel = null;
    }

    private void ShutdownWindow()
    {
        _shuttingDown = true;
        if (_window is { IsVisible: true }
            && _activeViewModel is { IsDirty: true }
            && !_dialogs.Confirm("Несохранённые изменения",
                "В редакторе шаблона есть несохранённые изменения. Закрыть текущий шаблон и открыть другой?"))
        {
            _window.Activate();
            return;
        }

        DetachActiveViewModel();
        _activeCaller = null;

        if (_window is not null)
        {
            _window.Closing -= OnWindowClosing;
            _window.Closed -= OnWindowClosed;
            _window.Close();
            _window = null;
        }
    }
}
