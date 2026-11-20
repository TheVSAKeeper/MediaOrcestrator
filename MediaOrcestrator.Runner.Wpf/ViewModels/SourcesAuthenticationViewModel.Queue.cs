using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class SourcesAuthenticationViewModel
{
    private readonly List<QueueStep> _queue = [];

    private int _queueIndex;

    public int SignInCount => _rows.Count(row => !row.IsDisabled && row.NeedsSignIn);
    public int AttentionCount => _rows.Count(row => row.NeedsAttention && GetSummaryCategory(row) != SourceAuthenticationStatus.None);
    public string TabText => AttentionCount == 0 ? "Авторизация" : $"Авторизация · {AttentionCount}";
    public string QueueButtonText => SignInCount == 0 ? "Войти по очереди" : $"Войти по очереди ({SignInCount})";
    public string QueuePreview => SignInCount == 0 ? "Нет источников, которым нужен вход."
        : string.Join(", ", _rows.Where(row => !row.IsDisabled && row.NeedsSignIn).Select(row => row.Title));
    public SourceAuthenticationRowViewModel? QueueCurrent => CurrentStep is { } step
        ? FindRow(step.SourceId) : null;
    public bool HasQueue => CurrentStep is not null;
    public string QueueText => QueueCurrent is { } row
        ? $"Шаг {_queueIndex + 1} из {_queue.Count} · {CurrentStep!.Title} ({row.Platform})"
        : string.Empty;
    public string QueueActionText => CurrentStep?.Saved == true ? "Проверить сессию" : QueueCurrent?.SignInText ?? "Войти";
    public bool CanContinueWithoutConfirmation => CurrentStep?.Saved == true && _signingIn is null;
    public string? QueueSummary => _queue.Count > 0 && !HasQueue
        ? string.Join(Environment.NewLine, _queue.Select(step => $"{step.Title} · {step.Outcome}"))
        : null;
    public string? QueueSummaryTitle => QueueSummary is not null ? $"Итог очереди · {_queue.Count} шагов" : null;

    private QueueStep? CurrentStep => _queueIndex < _queue.Count ? _queue[_queueIndex] : null;

    [RelayCommand(CanExecute = nameof(CanStartQueue), IncludeCancelCommand = true)]
    private async Task StartQueueAsync(CancellationToken cancellationToken)
    {
        _queue.Clear();
        foreach (var row in _rows.Where(row => !row.IsDisabled && row.NeedsSignIn))
        {
            var methods = row.MethodsNeedingSignIn.ToArray();
            if (methods.Length == 0)
            {
                _queue.Add(new(row.Source.Id, row.Title, null));
                continue;
            }

            _queue.AddRange(methods.Select(method => new QueueStep(row.Source.Id,
                $"{row.Title} · {method.Method.Title}", method.Method.Id)));
        }
        _queueIndex = 0;
        NotifyQueue();
        await ContinueQueueAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanContinueQueue), IncludeCancelCommand = true)]
    private async Task ContinueQueueAsync(CancellationToken cancellationToken)
    {
        var step = CurrentStep;
        var row = QueueCurrent;
        if (step is null || row is null)
        {
            return;
        }

        if (step.Saved)
        {
            BeginInteraction(row);
            try
            {
                await WaitForCheckAsync(row, cancellationToken);
                row.SetAwaiting(step.MethodId, true);
                await VerifySignInAsync(row, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogWarning("Не удалось подтвердить сохранённый вход {SourceId}: {ErrorType}", row.Source.Id, ex.GetType().Name);
                row.SignInError = "Не удалось подтвердить сохранённый вход. Повторите проверку.";
            }
            finally
            {
                EndInteraction();
            }
            return;
        }

        if (row.State.LastCheck is not null && row.StatusFor(step.MethodId) == SourceAuthenticationStatus.Valid)
        {
            FinishStep(step, "вход уже подтверждён");
            return;
        }

        await SignInCoreAsync(row, step.MethodId, step, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanSkipStep))]
    private void Skip() => FinishStep(CurrentStep, "пропущен");

    [RelayCommand(CanExecute = nameof(CanContinueWithoutConfirmation))]
    private void FinishUnconfirmed() => FinishStep(CurrentStep, "данные сохранены, вход не подтверждён");

    [RelayCommand(CanExecute = nameof(HasQueue))]
    private void StopQueue()
    {
        CancelStep();
        foreach (var step in _queue.Skip(_queueIndex))
        {
            step.Outcome = step.Saved ? "данные сохранены, вход не подтверждён" : "пропущен";
        }
        _queueIndex = _queue.Count;
        NotifyQueue();
    }

    [RelayCommand(CanExecute = nameof(CanCancelStep))]
    private void CancelStep()
    {
        StartQueueCommand.Cancel();
        ContinueQueueCommand.Cancel();
    }

    private void ApplyVerificationOutcome(SourceAuthenticationRowViewModel row, string? methodId, QueueStep? step)
    {
        var status = row.StatusFor(methodId);
        if (status == SourceAuthenticationStatus.Valid)
        {
            row.SetAwaiting(methodId, false);
            row.SignInError = null;
            FinishStep(step, "вход подтверждён");
        }
        else if (status == SourceAuthenticationStatus.Saved && row.Source.Type is not ISourceAuthenticationProbe)
        {
            row.SetAwaiting(methodId, false);
            FinishStep(step, "данные сохранены, вход не подтверждён");
        }
        else if (status is SourceAuthenticationStatus.Rejected or SourceAuthenticationStatus.Missing)
        {
            row.SetAwaiting(methodId, false);
            row.SignInError = "Площадка не подтвердила вход.";
            if (step is not null)
            {
                step.Saved = false;
            }
        }
    }

    private bool CanStartQueue() => CanOpenSourceSettings && !HasQueue && SignInCount > 0;
    private bool CanContinueQueue() => _signingIn is null && QueueCurrent is not null && !QueueCurrent.NeedsSettings && _editing.Count == 0;
    private bool CanSkipStep() => _signingIn is null && CurrentStep is { Saved: false };
    private bool CanCancelStep() => HasQueue && _signingIn is not null;

    private void FinishStep(QueueStep? step, string outcome)
    {
        if (step is null || CurrentStep != step)
        {
            return;
        }

        step.Outcome = outcome;
        _queueIndex++;
        AdvanceUnavailableSteps();
        NotifyQueue();
    }

    private void AdvanceUnavailableSteps()
    {
        while (CurrentStep is { } step && FindRow(step.SourceId) is not { IsDisabled: false })
        {
            step.Outcome = "источник удалён или выключен";
            _queueIndex++;
        }
    }

    private sealed class QueueStep(string sourceId, string title, string? methodId)
    {
        public string SourceId { get; } = sourceId;
        public string Title { get; } = title;
        public string? MethodId { get; } = methodId;
        public bool Saved { get; set; }
        public string? Outcome { get; set; }
    }
}
