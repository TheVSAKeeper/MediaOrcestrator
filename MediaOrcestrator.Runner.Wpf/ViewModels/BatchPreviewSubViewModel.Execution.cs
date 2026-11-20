using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class BatchPreviewSubViewModel
{
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var requests = BuildRequests();

        if (requests.Count == 0)
        {
            return;
        }

        var donor = SourceMode == BatchPreviewSourceMode.Donor ? SelectedDonor : null;
        var localFilePath = SourceMode == BatchPreviewSourceMode.File ? FilePath : null;
        var coverTemplate = SourceMode == BatchPreviewSourceMode.Template ? TemplatePicker.SelectedTemplate : null;

        var totalUnits = requests.Sum(r => r.Targets.Count);

        _applyCts?.Dispose();
        _applyCts = new();
        var token = _applyCts.Token;

        IsApplying = true;
        ProgressMax = Math.Max(totalUnits, 1);
        ProgressValue = 0;
        StatusText = "Подготовка";

        AppendJournal(OperationJournalLevel.Info,
            $"── Запуск: видео {requests.Count}, заливок {totalUnits} ──");

        var actionName = requests.Count == 1
            ? $"Обновление превью: «{Truncate(requests[0].Media.Title, 50)}»"
            : $"Обновление превью ({requests.Count} видео)";

        var running = _actionHolder.Register(actionName, "Подготовка", totalUnits, _applyCts, kind: ActionKind.Metadata);
        using var scope = _actionHolder.BeginScope(running);

        try
        {
            running.Status = "Авторизация площадок";

            if (!await EnsureAuthenticatedAsync(donor, requests, token))
            {
                StatusText = "Отменено – нет авторизации";
                AppendJournal(OperationJournalLevel.Warning, "Применение отменено: площадки не авторизованы");
                running.MarkCancelled("Нет авторизации площадок");
                return;
            }

            running.Status = $"0/{totalUnits}";

            var progress = new Progress<BatchPreviewProgress>(p => OnProgress(p, running));
            var uiContext = SynchronizationContext.Current ?? new();
            var processed = 0;

            var execOptions = new BatchPreviewExecutionOptions(StopOnError);

            var results = await Task.Run(() => _service.ApplyAsync(requests,
                    donor,
                    localFilePath,
                    coverTemplate,
                    progress,
                    result =>
                    {
                        Interlocked.Increment(ref processed);
                        var snapshot = processed;
                        uiContext.Post(_ => OnResultReported(result, snapshot, totalUnits, running), null);
                    },
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
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            StatusText = "Отменено пользователем";
            AppendJournal(OperationJournalLevel.Warning, "── Прервано пользователем ──");
            running.MarkCancelled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Пакетное обновление превью сорвалось");
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

    partial void OnIsApplyingChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditParameters));
    }

    private bool CanApply()
    {
        if (IsApplying || ActionableCount == 0)
        {
            return false;
        }

        return SourceMode switch
        {
            BatchPreviewSourceMode.Donor => SelectedDonor is not null,
            BatchPreviewSourceMode.File => HasFilePath,
            BatchPreviewSourceMode.Template => TemplatePicker.SelectedTemplate is not null,
            _ => false,
        };
    }

    private List<BatchPreviewRequest> BuildRequests()
    {
        var byMedia = new Dictionary<string, (Media Media, List<Source> Targets)>(StringComparer.Ordinal);

        foreach (var row in Rows)
        {
            if (!row.IsApplyChecked || row.IsApplied)
            {
                continue;
            }

            if (!byMedia.TryGetValue(row.Media.Id, out var bucket))
            {
                bucket = (row.Media, []);
                byMedia[row.Media.Id] = bucket;
            }

            bucket.Targets.Add(row.Target);
        }

        var requests = new List<BatchPreviewRequest>(byMedia.Count);
        foreach (var media in _medias)
        {
            if (byMedia.TryGetValue(media.Id, out var bucket))
            {
                requests.Add(new(bucket.Media, bucket.Targets));
            }
        }

        return requests;
    }

    private Task<bool> EnsureAuthenticatedAsync(Source? donor, IReadOnlyList<BatchPreviewRequest> requests, CancellationToken token)
    {
        var needed = new HashSet<string>(StringComparer.Ordinal);

        if (donor is not null)
        {
            needed.Add(donor.Id);
        }

        foreach (var target in requests.SelectMany(r => r.Targets))
        {
            needed.Add(target.Id);
        }

        if (needed.Count == 0)
        {
            return Task.FromResult(true);
        }

        var pending = _service.GetUnauthenticatedSources(needed);

        if (pending.Count == 0)
        {
            return Task.FromResult(true);
        }

        var message = "Перед обновлением превью нужно войти на площадки:" + Environment.NewLine
            + string.Join(Environment.NewLine, pending.Select(p => "• " + p.Title)) + Environment.NewLine
            + Environment.NewLine
            + "В WPF-версии auth-flow ещё не подключён. Войдите через WinForms-Runner и попробуйте снова.";

        _dialogs.Warning("Требуется авторизация", message);

        token.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    private void OnProgress(BatchPreviewProgress progress, ActionHolder.RunningAction running)
    {
        var current = string.IsNullOrEmpty(progress.CurrentTitle) ? "..." : Truncate(progress.CurrentTitle, 50);

        if (progress.Processed < progress.Total)
        {
            StatusText = $"Обработка {progress.Processed + 1}/{progress.Total}: {current}";
        }

        if (running.State == ActionState.Running)
        {
            running.Subtitle = current;
        }
    }

    private void OnResultReported(BatchPreviewResult result, int processed, int totalUnits, ActionHolder.RunningAction running)
    {
        var row = Rows.FirstOrDefault(r =>
            r.Media.Id == result.Media.Id && r.Target.Id == result.Target.Id);

        if (row is not null)
        {
            row.IsApplied = true;
            row.IsApplyError = !result.Success;
            row.StatusText = result.Success ? "Готово" : result.ErrorMessage ?? "Ошибка";
            row.ResultTooltip = result.Success ? null : result.ErrorMessage;
        }

        var title = Truncate(result.Media.Title, 50);
        var targetTitle = result.Target.TitleFull;

        if (result.Success)
        {
            AppendJournal(OperationJournalLevel.Success, $"✓ «{title}» → {targetTitle}");
        }
        else
        {
            AppendJournal(OperationJournalLevel.Error,
                $"✗ «{title}» → {targetTitle} – {result.ErrorMessage ?? "ошибка"}");
        }

        ProgressMax = Math.Max(totalUnits, 1);
        ProgressValue = Math.Min(processed, ProgressMax);

        if (running.State == ActionState.Running)
        {
            running.ProgressMax = totalUnits;
            running.SetProgress(Math.Min(processed, totalUnits));
            running.Status = $"{Math.Min(processed, totalUnits)}/{totalUnits}";
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
}
