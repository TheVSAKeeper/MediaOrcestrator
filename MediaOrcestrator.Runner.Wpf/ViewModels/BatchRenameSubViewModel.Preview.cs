using MediaOrcestrator.Domain;
using System.Text;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class BatchRenameSubViewModel
{
    private void SchedulePreview()
    {
        if (IsApplying || !HasMedias)
        {
            return;
        }

        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void OnPreviewTimerTick()
    {
        _previewTimer.Stop();
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_medias.Count == 0)
        {
            RegexError = null;
            RecountActionable();
            return;
        }

        var options = CurrentOptions();
        var previews = _service.Preview(_medias, options);
        string? firstError = null;

        foreach (var preview in previews)
        {
            var row = Rows.FirstOrDefault(r => ReferenceEquals(r.Media, preview.Media));

            if (row is null)
            {
                continue;
            }

            row.Sources = preview.Sources;
            row.NewTitle = preview.NewTitle;
            row.PreviewError = preview.Error;

            if (preview.Error is not null)
            {
                firstError ??= preview.Error;
            }

            UpdateRowSummary(row, preview);
        }

        RegexError = firstError;
        RecountActionable();
    }

    private static void UpdateRowSummary(BatchRenameRowViewModel row, BatchRenamePreview preview)
    {
        row.SourceChips = BuildSourceChips(preview.Sources);

        var updatable = preview.Sources.Where(s => s.CanUpdate).ToList();

        if (updatable.Count == 0)
        {
            row.SourcesSummary = "–";
            row.SourcesTooltip = "Нет площадок, доступных для переименования";
            row.HasUpdatableSources = false;
            row.IsOutOfSync = false;
            return;
        }

        var drift = updatable
            .Where(s => !s.InSync)
            .Select(s => s.SourceTitle)
            .ToList();

        row.HasUpdatableSources = true;
        row.IsOutOfSync = drift.Count > 0;

        row.SourcesSummary = drift.Count > 0
            ? "✗ " + string.Join(", ", drift)
            : "✓ синхронно";

        var tooltip = new StringBuilder();

        foreach (var source in preview.Sources)
        {
            var mark = !source.CanUpdate ? "•" : source.InSync ? "✓" : "✗";
            tooltip.Append(mark).Append(' ').Append(source.SourceTitle);

            if (!source.CanUpdate)
            {
                tooltip.Append(" – ").Append(source.SkipReason);
            }
            else if (!source.InSync)
            {
                tooltip.Append(" – сейчас «").Append(Truncate(source.CurrentTitle, 50)).Append('»');
            }
            else
            {
                tooltip.Append(" – актуально");
            }

            tooltip.AppendLine();
        }

        row.SourcesTooltip = tooltip.ToString().TrimEnd();
    }

    private static IReadOnlyList<OperationSourceChipViewModel> BuildSourceChips(IReadOnlyList<BatchRenameSourcePreview> sources)
    {
        return sources
            .Select(source =>
            {
                if (!source.CanUpdate)
                {
                    return new OperationSourceChipViewModel
                    {
                        Text = source.SourceTitle,
                        State = OperationSourceChipState.Skipped,
                        Tooltip = string.IsNullOrEmpty(source.SkipReason)
                            ? "Переименование не поддерживается"
                            : source.SkipReason,
                    };
                }

                if (source.InSync)
                {
                    return new OperationSourceChipViewModel
                    {
                        Text = source.SourceTitle,
                        State = OperationSourceChipState.Synced,
                        Tooltip = "Название актуально",
                    };
                }

                return new OperationSourceChipViewModel
                {
                    Text = source.SourceTitle,
                    State = OperationSourceChipState.OutOfSync,
                    Tooltip = $"Сейчас: «{Truncate(source.CurrentTitle, 60)}»",
                };
            })
            .ToList();
    }

    private void RecountActionable()
    {
        ActionableCount = Rows.Count(r => r.IsActionable);
    }

    private BatchRenameOptions CurrentOptions()
    {
        return new(Find, Replace, Mode, IgnoreCase, BuildAllowedIds());
    }

    private IReadOnlyCollection<string>? BuildAllowedIds()
    {
        if (Sources.Count == 0)
        {
            return null;
        }

        if (Sources.All(s => s.IsChecked))
        {
            return null;
        }

        return Sources
            .Where(s => s.IsChecked)
            .Select(s => s.SourceId)
            .ToHashSet(StringComparer.Ordinal);
    }
}
