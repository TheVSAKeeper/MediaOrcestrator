using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class ResolveConflictsSubViewModel
{
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var selected = Groups.Where(g => g is { IsMergeChecked: true, IsResolved: true, IsApplied: false }).ToList();

        if (selected.Count == 0)
        {
            return;
        }

        var totalRemoved = selected.Sum(g => Math.Max(g.ActiveRows.Count - 1, 0));
        var confirmed = _dialogs.ConfirmWarning(
            $"Объединить {selected.Count} групп?",
            $"Будет удалено {totalRemoved} медиа. Конфликты источников разрешаются вашим выбором. Это действие нельзя отменить.");

        if (!confirmed)
        {
            return;
        }

        _applyCts?.Dispose();
        _applyCts = new();
        var token = _applyCts.Token;

        IsApplying = true;
        ProgressMax = Math.Max(selected.Count, 1);
        ProgressValue = 0;
        StatusText = "Применение слияний...";

        AppendJournal(OperationJournalLevel.Info,
            $"── Запуск: групп {selected.Count}, к удалению {totalRemoved} ──");

        var actionName = selected.Count == 1
            ? $"Разбор конфликта: «{Truncate(selected[0].Title, 50)}»"
            : $"Разбор конфликтов ({selected.Count} групп)";

        var running = _actionHolder.Register(actionName, "Применение", selected.Count, _applyCts, kind: ActionKind.Metadata);
        using var scope = _actionHolder.BeginScope(running);

        var success = 0;
        var failed = 0;

        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var group = selected[i];

                running.Status = $"{i + 1}/{selected.Count}: {Truncate(group.Title, 50)}";
                StatusText = running.Status;

                try
                {
                    await Task.Run(() => ApplyGroup(group), token);

                    group.IsApplied = true;
                    group.IsApplyError = false;
                    group.ResultText = "✓ Объединено";
                    AppendJournal(OperationJournalLevel.Success,
                        $"✓ «{Truncate(group.Title, 50)}» – объединено медиа: {group.ActiveRows.Count}");
                    success++;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    group.IsApplied = true;
                    group.IsApplyError = true;
                    group.ResultText = "✗ " + ex.Message;
                    AppendJournal(OperationJournalLevel.Error,
                        $"✗ «{Truncate(group.Title, 50)}» – {ex.Message}");
                    failed++;

                    _logger.LogError(ex, "Не удалось разобрать конфликт группы '{Key}'", group.Title);

                    if (StopOnError)
                    {
                        break;
                    }
                }

                ProgressValue = i + 1;
                running.SetProgress(i + 1);
            }

            StatusText = failed == 0
                ? $"Готово: объединено {success} из {selected.Count}"
                : $"Готово: {success} успешно, {failed} с ошибками";

            AppendJournal(failed == 0 ? OperationJournalLevel.Success : OperationJournalLevel.Error,
                $"── Готово: успешно {success}, с ошибками {failed} ──");

            if (failed == 0)
            {
                running.Finish($"Объединено {success} из {selected.Count}");
            }
            else
            {
                running.Fail($"Объединено {success}, ошибок {failed}");
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
            _logger.LogError(ex, "Пакетный разбор конфликтов сорвался");
            StatusText = "Ошибка: " + ex.Message;
            AppendJournal(OperationJournalLevel.Error, "Ошибка: " + ex.Message);
            running.Fail(ex.Message, ex);
        }
        finally
        {
            IsApplying = false;
            ProgressValue = ProgressMax;
            RecountReady();
        }
    }

    private void ApplyGroup(ConflictGroupViewModel group)
    {
        var remaining = group.ActiveRows.Select(r => r.Media).ToList();
        var target = group.TargetRow?.Media
            ?? throw new InvalidOperationException("Не выбрано целевое медиа");

        var winners = group.Conflicts
            .Where(c => c.SelectedChoice != null)
            .ToDictionary(c => c.SourceId, c => c.SelectedChoice!.ExternalId, StringComparer.Ordinal);

        var preview = _mergeService.BuildPreview(remaining, target, winners);

        _mergeService.Apply(preview, acceptConflicts: true);
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

    private bool CanApply() => !IsApplying && !IsSearching && ReadyGroups > 0;

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
