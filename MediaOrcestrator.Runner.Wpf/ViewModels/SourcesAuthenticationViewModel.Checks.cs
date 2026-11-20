using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class SourcesAuthenticationViewModel
{
    private readonly SemaphoreSlim _checkSlots = new(3);
    private readonly Dictionary<string, CheckWork> _checks = [];
    private readonly HashSet<string> _editing = [];

    private bool _active;

    internal void SetActive(bool active)
    {
        _active = active;
        ScheduleChecks();
    }

    internal void Invalidate(string sourceId)
    {
        authentication.Invalidate(sourceId);
        if (FindRow(sourceId) is { } row)
        {
            _rows.Remove(row);
            CancelCheck(sourceId);
        }
    }

    internal async Task PauseForEditAsync(string sourceId)
    {
        _editing.Add(sourceId);
        NotifyResults();
        if (_checks.TryGetValue(sourceId, out var check))
        {
            check.Cancellation.Cancel();
            await check.Execution;
        }
    }

    internal void ResumeAfterEdit(string sourceId)
    {
        _editing.Remove(sourceId);
        ScheduleChecks();
        NotifyResults();
    }

    [RelayCommand]
    internal Task CheckAllAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(_rows.Where(row => !row.IsDisabled).Select(row => BeginCheck(row, cancellationToken)).ToArray());

    [RelayCommand(CanExecute = nameof(CanCheckRow), AllowConcurrentExecutions = true)]
    private Task CheckAsync(SourceAuthenticationRowViewModel? row) =>
        row is null ? Task.CompletedTask : BeginCheck(row, CancellationToken.None);

    [RelayCommand(CanExecute = nameof(IsChecking))]
    private void CancelChecks()
    {
        foreach (var work in _checks.Values.ToArray())
        {
            work.Cancellation.Cancel();
        }
    }

    private void ScheduleChecks()
    {
        if (!_active)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var row in _rows.Where(row => !row.IsDisabled).ToArray())
        {
            var age = row.Check.Status == SourceAuthenticationStatus.CheckFailed ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(15);
            if (row.State.CheckedAt is { } checkedAt && now - checkedAt < age ||
                row.State.LastCheck is null && row.State.Local.Status is SourceAuthenticationStatus.Missing or SourceAuthenticationStatus.SettingsRequired)
            {
                continue;
            }

            _ = BeginCheck(row, CancellationToken.None);
        }
    }

    private Task BeginCheck(SourceAuthenticationRowViewModel row, CancellationToken cancellationToken)
    {
        if (_signingIn?.Source.Id == row.Source.Id || _editing.Contains(row.Source.Id))
        {
            return Task.CompletedTask;
        }

        _checks.TryGetValue(row.Source.Id, out var previous);
        if (previous is { Cancellation.IsCancellationRequested: false } && previous.Row == row)
        {
            return previous.Execution;
        }

        var work = new CheckWork(row, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
        _checks[row.Source.Id] = work;
        row.IsChecking = true;
        NotifyResults();
        work.Execution = RunCheckAsync(work, previous?.Execution);
        return work.Execution;
    }

    private async Task RunCheckAsync(CheckWork work, Task? previous)
    {
        var row = work.Row;
        var token = work.Cancellation.Token;
        try
        {
            if (previous is not null)
            {
                await previous;
            }

            await _checkSlots.WaitAsync(token);
            try
            {
                await CheckRowAsync(row, token);
            }
            finally
            {
                _checkSlots.Release();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning("Не удалось обновить состояние авторизации {SourceId}: {ErrorType}", row.Source.Id, ex.GetType().Name);
            if (_rows.Contains(row) && !token.IsCancellationRequested)
            {
                row.Refresh(row.Source, row.State with { LastCheck = new(SourceAuthenticationStatus.CheckFailed) });
            }
        }
        finally
        {
            work.Cancellation.Dispose();
            if (_checks.GetValueOrDefault(row.Source.Id) == work)
            {
                _checks.Remove(row.Source.Id);
                row.IsChecking = false;
            }

            NotifyResults();
        }
    }

    private async Task CheckRowAsync(SourceAuthenticationRowViewModel row, CancellationToken token)
    {
        var source = row.Source;
        var state = await authentication.CheckAndRecordAsync(source, token);
        token.ThrowIfCancellationRequested();
        if (_rows.Contains(row))
        {
            row.Refresh(row.Source, state);
            if (row.AwaitingVerification)
            {
                var step = CurrentStep is { Saved: true } current && current.SourceId == row.Source.Id ? current : null;
                foreach (var methodId in row.PendingMethods.ToArray())
                {
                    ApplyVerificationOutcome(row, methodId, step is not null && step.MethodId == methodId ? step : null);
                }
            }

            NotifyResults();
        }
    }

    private async Task WaitForCheckAsync(SourceAuthenticationRowViewModel row, CancellationToken cancellationToken)
    {
        if (_checks.TryGetValue(row.Source.Id, out var checking))
        {
            checking.Cancellation.Cancel();
            await checking.Execution.WaitAsync(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private bool CanCheckRow(SourceAuthenticationRowViewModel? row) =>
        row is not null && _rows.Contains(row) && _signingIn?.Source.Id != row.Source.Id
        && !_editing.Contains(row.Source.Id) && !row.IsChecking;

    private void CancelCheck(string sourceId)
    {
        if (_checks.TryGetValue(sourceId, out var work))
        {
            work.Cancellation.Cancel();
        }
    }

    private sealed class CheckWork(SourceAuthenticationRowViewModel row, CancellationTokenSource cancellation)
    {
        public SourceAuthenticationRowViewModel Row { get; } = row;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Execution { get; set; } = Task.CompletedTask;
    }
}
