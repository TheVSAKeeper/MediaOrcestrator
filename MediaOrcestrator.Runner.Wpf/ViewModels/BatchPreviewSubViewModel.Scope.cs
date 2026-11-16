using MediaOrcestrator.Domain;
using System.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class BatchPreviewSubViewModel
{
    partial void OnSourceModeChanged(BatchPreviewSourceMode value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(SourceModeKey, value.ToString());
        }

        RebuildTargets();
        RebuildRows();
        RefreshPreviewTile();
    }

    partial void OnSelectedDonorChanged(Source? value)
    {
        RebuildTargets();
        RebuildRows();
        RefreshPreviewTile();
    }

    partial void OnFilePathChanged(string? value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(LastFilePathKey, value ?? string.Empty);
        }

        OnPropertyChanged(nameof(HasFilePath));
        ApplyCommand.NotifyCanExecuteChanged();
        RefreshPreviewTile();
    }

    partial void OnStopOnErrorChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(StopOnErrorKey, value ? "true" : "false");
        }
    }

    partial void OnIsCompactChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(CompactKey, value ? "true" : "false");
        }
    }

    partial void OnActionableCountChanged(int value)
    {
        OnPropertyChanged(nameof(ApplyButtonText));
    }

    private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressEvents || e.PropertyName != nameof(SourceCheckOptionViewModel.IsChecked))
        {
            return;
        }

        RebuildRows();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressEvents || e.PropertyName != nameof(BatchPreviewRowViewModel.IsApplyChecked))
        {
            return;
        }

        RecountActionable();
    }

    private void RebuildDonors()
    {
        var previousId = SelectedDonor?.Id;
        Donors.ReplaceAll(_service.GetAvailableDonors(_medias));

        SelectedDonor = previousId is not null
            ? Donors.FirstOrDefault(d => d.Id == previousId) ?? Donors.FirstOrDefault()
            : Donors.FirstOrDefault();
    }

    private void RebuildTargets()
    {
        _suppressEvents = true;
        try
        {
            var previousChecks = Targets.ToDictionary(s => s.SourceId, s => s.IsChecked, StringComparer.Ordinal);

            foreach (var target in Targets)
            {
                target.PropertyChanged -= OnTargetPropertyChanged;
            }

            var donor = SourceMode == BatchPreviewSourceMode.Donor ? SelectedDonor : null;
            var newTargets = _service.GetAvailableTargets(_medias, donor)
                .Select(target => new SourceCheckOptionViewModel
                {
                    SourceId = target.Id,
                    Title = target.TitleFull,
                    IsChecked = !previousChecks.TryGetValue(target.Id, out var was) || was,
                })
                .ToList();

            foreach (var target in newTargets)
            {
                target.PropertyChanged += OnTargetPropertyChanged;
            }

            Targets.ReplaceAll(newTargets);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void RebuildRows()
    {
        _suppressEvents = true;
        try
        {
            foreach (var row in Rows)
            {
                row.PropertyChanged -= OnRowPropertyChanged;
            }

            var donor = SourceMode == BatchPreviewSourceMode.Donor ? SelectedDonor : null;
            var available = _service.GetAvailableTargets(_medias, donor);
            var checkedIds = Targets
                .Where(t => t.IsChecked)
                .Select(t => t.SourceId)
                .ToHashSet(StringComparer.Ordinal);

            var selectedTargets = available.Where(t => checkedIds.Contains(t.Id)).ToList();
            var newRows = new List<BatchPreviewRowViewModel>();

            foreach (var media in _medias)
            {
                foreach (var target in selectedTargets)
                {
                    if (media.Sources.All(s => s.SourceId != target.Id))
                    {
                        continue;
                    }

                    newRows.Add(new()
                    {
                        Media = media,
                        Target = target,
                    });
                }
            }

            foreach (var row in newRows)
            {
                row.PropertyChanged += OnRowPropertyChanged;
            }

            Rows.ReplaceAll(newRows);
        }
        finally
        {
            _suppressEvents = false;
        }

        RecountActionable();
    }

    private void RecountActionable()
    {
        ActionableCount = Rows.Count(r => r.IsApplyChecked && !r.IsApplied);
    }

    private void SetAllTargets(bool value)
    {
        _suppressEvents = true;
        try
        {
            foreach (var target in Targets)
            {
                target.IsChecked = value;
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        RebuildRows();
    }

    private void SetAllRowChecks(bool value)
    {
        _suppressEvents = true;
        try
        {
            foreach (var row in Rows)
            {
                row.IsApplyChecked = value;
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        RecountActionable();
    }
}
