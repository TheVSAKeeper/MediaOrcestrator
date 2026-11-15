using CommunityToolkit.Mvvm.ComponentModel;
using KeepShell.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Windows.Input;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.Services.Diagnostics;

public abstract partial class PollingDiagnosticsCardBase : ObservableObject, IDiagnosticsCard, IDisposable
{
    private readonly ILogger _logger;
    private readonly DispatcherTimer _timer;
    private bool _disposed;
    private bool _refreshRunning;

    [ObservableProperty]
    private DiagnosticsCardState _state = DiagnosticsCardState.Unknown;

    [ObservableProperty]
    private IReadOnlyList<DiagnosticsCardRow> _rows = [];

    protected PollingDiagnosticsCardBase(ILogger logger, TimeSpan? interval = null)
    {
        _logger = logger;
        _timer = new DispatcherTimer
        {
            Interval = interval ?? TimeSpan.FromSeconds(5),
        };
        _timer.Tick += OnTimerTick;
    }

    public abstract string Title { get; }

    public virtual ICommand? Command => null;

    public virtual string? CommandCaption => null;

    public void SetActive(bool active)
    {
        if (_disposed)
        {
            return;
        }

        if (active)
        {
            TriggerRefresh();
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }
        else
        {
            _timer.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    protected abstract Task<(DiagnosticsCardState State, IReadOnlyList<DiagnosticsCardRow> Rows)> SampleAsync();

    protected static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            <= 0 => "0 Б",
            < 1024 => $"{bytes} Б",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} КБ",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} МБ",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} ГБ",
        };
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        TriggerRefresh();
    }

    private async void TriggerRefresh()
    {
        if (_disposed || _refreshRunning)
        {
            return;
        }

        _refreshRunning = true;

        try
        {
            var (nextState, nextRows) = await SampleAsync();

            if (!_disposed)
            {
                State = nextState;
                Rows = nextRows;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка обновления карточки диагностики {CardType}", GetType().Name);

            if (!_disposed)
            {
                State = DiagnosticsCardState.Error;
                Rows = [new("Ошибка", ex.Message)];
            }
        }
        finally
        {
            _refreshRunning = false;
        }
    }
}
