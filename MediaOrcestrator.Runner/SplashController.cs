namespace MediaOrcestrator.Runner;

public sealed class SplashController : IDisposable
{
    private readonly Thread _uiThread;
    private readonly ManualResetEventSlim _ready = new();
    private SplashForm? _form;
    private bool _closed;

    public SplashController(string version, int totalSteps, string initialStatus)
    {
        _uiThread = new(() =>
        {
            _form = new();
            _form.SetVersion(version);
            _form.SetMaxSteps(totalSteps);
            _form.SetStatus(initialStatus);
            _form.HandleCreated += (_, _) => _ready.Set();
            _form.FormClosed += (_, _) => _ready.Set();
            Application.Run(_form);
        })
        {
            Name = "SplashUI",
            IsBackground = true,
        };

        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void Step(string message)
    {
        Marshal(form => form.Step(message));
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        var form = _form;

        if (form is { IsDisposed: false })
        {
            try
            {
                form.BeginInvoke(form.Close);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        _uiThread.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Close();
        _ready.Dispose();
    }

    private void Marshal(Action<SplashForm> action)
    {
        var form = _form;
        if (form == null || form.IsDisposed)
        {
            return;
        }

        try
        {
            form.BeginInvoke(action, form);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
