using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class BatchRenameSubViewModel
{
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var requests = BuildRequests();

        if (requests.Count == 0)
        {
            return;
        }

        var actionName = _medias.Count == 1
            ? $"Переименование: «{Truncate(_medias[0].Title, 50)}»"
            : $"Переименование ({requests.Count})";

        _applyCts?.Dispose();
        _applyCts = new();
        var token = _applyCts.Token;

        IsApplying = true;
        ProgressMax = Math.Max(requests.Count, 1);
        ProgressValue = 0;
        StatusText = "Подготовка";
        AppendJournal(OperationJournalLevel.Info, $"── Запуск: записей {requests.Count} ──");

        var running = _actionHolder.Register(actionName, "Подготовка", requests.Count, _applyCts, kind: ActionKind.Metadata);
        using var scope = _actionHolder.BeginScope(running);

        try
        {
            running.Status = "Авторизация площадок";

            if (!await EnsureAuthenticatedAsync(token))
            {
                StatusText = "Отменено – нет авторизации";
                AppendJournal(OperationJournalLevel.Warning, "Применение отменено: площадки не авторизованы");
                running.MarkCancelled("Нет авторизации площадок");
                return;
            }

            running.Status = $"0/{requests.Count}";

            var progress = new Progress<BatchRenameProgress>(p => OnProgress(p, running));
            var execOptions = new BatchRenameExecutionOptions(StopOnError);

            var results = await Task.Run(() => _service.ApplyAsync(requests,
                    progress,
                    result => _dispatcher.Invoke(() => OnResultReported(result)),
                    token,
                    execOptions),
                token);

            var success = results.Count(r => r.Success);
            var failed = results.Count - success;

            StatusText = failed == 0
                ? $"Готово: применено {success} из {results.Count}"
                : $"Готово: {success} успешно, {failed} с ошибками";

            AppendJournal(failed == 0 ? OperationJournalLevel.Success : OperationJournalLevel.Error,
                $"── Готово: успешно {success}, с ошибками {failed} ──");

            if (failed == 0)
            {
                running.Finish($"Применено {success} из {results.Count}");
            }
            else
            {
                running.Fail($"Применено {success}, ошибок {failed}");
            }

            SchedulePreview();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Отменено пользователем";
            AppendJournal(OperationJournalLevel.Warning, "── Прервано пользователем ──");
            running.MarkCancelled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Пакетное переименование сорвалось");
            StatusText = "Ошибка: " + ex.Message;
            AppendJournal(OperationJournalLevel.Error, "Ошибка: " + ex.Message);
            running.Fail(ex.Message, ex);
        }
        finally
        {
            IsApplying = false;
            ProgressValue = ProgressMax;
        }
    }

    [RelayCommand(CanExecute = nameof(IsApplyingValue))]
    private void Cancel()
    {
        try
        {
            _applyCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool IsApplyingValue() => IsApplying;

    private bool CanApply() => !IsApplying && ActionableCount > 0 && RegexError is null;

    partial void OnIsApplyingChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditParameters));
    }

    private List<BatchRenameRequest> BuildRequests()
    {
        var allowed = BuildAllowedIds();
        var list = new List<BatchRenameRequest>();

        foreach (var row in Rows)
        {
            if (!row.IsActionable)
            {
                continue;
            }

            list.Add(new(row.Media, row.NewTitle, null, allowed));
        }

        return list;
    }

    private Task<bool> EnsureAuthenticatedAsync(CancellationToken token)
    {
        var allowed = BuildAllowedIds() ?? Sources.Select(s => s.SourceId).ToHashSet(StringComparer.Ordinal);

        if (allowed.Count == 0)
        {
            return Task.FromResult(true);
        }

        var pending = _service.GetUnauthenticatedSources(allowed);

        if (pending.Count == 0)
        {
            return Task.FromResult(true);
        }

        var message = "Перед переименованием нужно войти на площадки:" + Environment.NewLine
            + string.Join(Environment.NewLine, pending.Select(p => "• " + p.Title)) + Environment.NewLine
            + Environment.NewLine
            + "В WPF-версии auth-flow ещё не подключён. Войдите через WinForms-Runner и попробуйте снова.";

        _dispatcher.Invoke(() => _dialogs.Warning("Требуется авторизация", message));

        token.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    private void OnProgress(BatchRenameProgress progress, ActionHolder.RunningAction running)
    {
        if (progress.Total > 0)
        {
            ProgressMax = progress.Total;
            ProgressValue = Math.Min(progress.Processed, progress.Total);
        }

        var current = string.IsNullOrEmpty(progress.CurrentTitle) ? "..." : Truncate(progress.CurrentTitle, 50);
        StatusText = $"Применение {Math.Min(progress.Processed + 1, progress.Total)}/{progress.Total}: {current}";

        if (running.State == ActionState.Running && progress.Total > 0)
        {
            running.ProgressMax = progress.Total;
            running.SetProgress(Math.Min(progress.Processed, progress.Total));
            running.Status = $"{Math.Min(progress.Processed, progress.Total)}/{progress.Total}";
        }
    }

    private void OnResultReported(BatchRenameResult result)
    {
        var row = Rows.FirstOrDefault(r => r.Media.Id == result.Media.Id);

        if (row is not null)
        {
            row.IsApplied = true;
            row.IsApplyError = !result.Success;
            row.ResultTooltip = BuildResultTooltip(result);
        }

        if (result.Success)
        {
            var changed = !string.Equals(result.OldTitle, result.NewTitle, StringComparison.Ordinal);
            AppendJournal(OperationJournalLevel.Success,
                changed
                    ? $"✓ «{Truncate(result.OldTitle, 50)}» → «{Truncate(result.NewTitle, 50)}»"
                    : $"✓ «{Truncate(result.NewTitle, 50)}» – площадки досинхронизированы");
        }
        else
        {
            AppendJournal(OperationJournalLevel.Error,
                $"✗ «{Truncate(result.OldTitle, 50)}» – {result.ErrorMessage ?? "ошибка"}");
        }

        foreach (var source in result.Sources)
        {
            AppendJournal(SourceOutcomeLevel(source.Outcome),
                $"{source.SourceTitle} – {OutcomeText(source)}",
                indent: 1);
        }
    }

    private void AppendJournal(OperationJournalLevel level, string text, int indent = 0)
    {
        Journal.Add(new(DateTime.Now, level, text, indent));

        while (Journal.Count > JournalMaxEntries)
        {
            Journal.RemoveAt(0);
        }
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value ?? string.Empty;
        }

        return value.AsSpan(0, max - 1).ToString() + "…";
    }

    private static string OutcomeText(BatchRenameSourceResult source)
    {
        return source.Outcome switch
        {
            BatchRenameSourceOutcome.Updated => "обновлено",
            BatchRenameSourceOutcome.AlreadyUpToDate => "уже актуально",
            BatchRenameSourceOutcome.Skipped => $"пропуск ({source.Message})",
            BatchRenameSourceOutcome.NotSupported => "обновление не поддерживается",
            BatchRenameSourceOutcome.VerificationFailed => "название на площадке не изменилось",
            BatchRenameSourceOutcome.Failed => $"ошибка – {source.Message}",
            _ => source.Message ?? string.Empty,
        };
    }

    private static OperationJournalLevel SourceOutcomeLevel(BatchRenameSourceOutcome outcome)
    {
        return outcome switch
        {
            BatchRenameSourceOutcome.Updated => OperationJournalLevel.Success,
            BatchRenameSourceOutcome.AlreadyUpToDate => OperationJournalLevel.Info,
            BatchRenameSourceOutcome.Skipped => OperationJournalLevel.Info,
            BatchRenameSourceOutcome.NotSupported => OperationJournalLevel.Warning,
            BatchRenameSourceOutcome.VerificationFailed => OperationJournalLevel.Error,
            BatchRenameSourceOutcome.Failed => OperationJournalLevel.Error,
            _ => OperationJournalLevel.Info,
        };
    }

    private static string BuildResultTooltip(BatchRenameResult result)
    {
        var tip = new StringBuilder();
        tip.AppendLine(result.Success ? "Применено" : "Ошибка: " + (result.ErrorMessage ?? "неизвестно"));

        foreach (var source in result.Sources)
        {
            tip.Append("• ").Append(source.SourceTitle).Append(" – ").AppendLine(OutcomeText(source));
        }

        return tip.ToString().TrimEnd();
    }
}
