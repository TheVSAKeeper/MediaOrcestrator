using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MergeDuplicatesSubViewModel
{
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var selected = Groups.Where(g => g.IsMergeChecked && !g.IsApplied).ToList();

        if (selected.Count == 0)
        {
            return;
        }

        var totalRemoved = selected.Sum(g => g.ToBeRemoved);
        var confirmed = _dialogs.ConfirmWarning(
            $"Объединить {selected.Count} групп?",
            $"Будет удалено {totalRemoved} медиа. Это действие нельзя отменить.");

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
            ? $"Слияние: «{selected[0].TargetTitle}»"
            : $"Слияние ({selected.Count} групп)";

        var running = _actionHolder.Register(actionName, "Применение", selected.Count, _applyCts, kind: ActionKind.Metadata);
        using var scope = _actionHolder.BeginScope(running);

        var success = 0;
        var failed = 0;

        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var row = selected[i];

                running.Status = $"{i + 1}/{selected.Count}: {Truncate(row.TargetTitle, 50)}";
                StatusText = running.Status;

                try
                {
                    await Task.Run(() =>
                    {
                        var preview = _mergeService.BuildPreview(row.Group.Medias, row.Group.SuggestedTarget);

                        _mergeService.Apply(preview, acceptConflicts: false);
                    }, token);

                    row.IsApplied = true;
                    row.IsApplyError = false;
                    row.ResultText = "Объединено";
                    AppendJournal(OperationJournalLevel.Success,
                        $"✓ «{Truncate(row.TargetTitle, 50)}» – объединено медиа: {row.MediaCount}");
                    success++;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    row.IsApplied = true;
                    row.IsApplyError = true;
                    row.ResultText = ex.Message;
                    row.ResultTooltip = ex.Message;
                    AppendJournal(OperationJournalLevel.Error,
                        $"✗ «{Truncate(row.TargetTitle, 50)}» – {ex.Message}");
                    failed++;

                    _logger.LogError(ex, "Не удалось объединить группу '{Key}'", row.NormalizedKey);

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
            _logger.LogError(ex, "Пакетное слияние сорвалось");
            StatusText = "Ошибка: " + ex.Message;
            AppendJournal(OperationJournalLevel.Error, "Ошибка: " + ex.Message);
            running.Fail(ex.Message, ex);
        }
        finally
        {
            IsApplying = false;
            ProgressValue = ProgressMax;
            RecountChecked();
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

    private bool CanApply() => !IsApplying && !IsSearching && CheckedGroups > 0;

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

    private static string FormatMedia(Media media, List<Source> allSources)
    {
        var sourceNames = (media.Sources ?? [])
            .Select(s => allSources.FirstOrDefault(x => x.Id == s.SourceId)?.Title ?? s.SourceId);

        return $"{media.Title} [{string.Join(", ", sourceNames)}]";
    }
}
