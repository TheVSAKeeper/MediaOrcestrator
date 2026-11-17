using System.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MediaViewModel
{
    public void UpdateViewportWidth(double windowWidth)
    {
        if (double.IsNaN(windowWidth) || windowWidth <= 0)
        {
            return;
        }

        var isNarrow = windowWidth < AutoCollapseWindowWidth;

        if (isNarrow != _isNarrowViewport)
        {
            _manualOverrideOnNarrow = false;
        }

        _isNarrowViewport = isNarrow;
        ApplyResponsiveInspectorState();
    }

    private void CollapseInspectorFromUser()
    {
        IsInspectorCollapsed = true;
        _userCollapsedPreference = true;
        _autoCollapsedByViewport = false;

        if (_isNarrowViewport)
        {
            _manualOverrideOnNarrow = true;
        }
    }

    private void OnRequestExpandInspector(object? sender, EventArgs e)
    {
        _autoCollapsedByViewport = false;
        IsInspectorCollapsed = false;
        _userCollapsedPreference = false;

        if (_isNarrowViewport)
        {
            _manualOverrideOnNarrow = true;
        }
    }

    private void OnGridPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaGridViewModel.HasSelection) or nameof(MediaGridViewModel.Focused))
        {
            ApplyResponsiveInspectorState();
        }
    }

    private void ApplyResponsiveInspectorState()
    {
        var hasSelection = Grid.HasSelection || Grid.Focused is not null;

        if (!_isNarrowViewport || hasSelection)
        {
            if (_autoCollapsedByViewport)
            {
                SetInspectorCollapsedWithoutPersist(_userCollapsedPreference);
                _autoCollapsedByViewport = false;
            }

            return;
        }

        if (!_manualOverrideOnNarrow && !IsInspectorCollapsed)
        {
            SetInspectorCollapsedWithoutPersist(true);
            _autoCollapsedByViewport = true;
        }
    }

    private void SetInspectorCollapsedWithoutPersist(bool collapsed)
    {
        _suppressPrefPersist = true;

        try
        {
            IsInspectorCollapsed = collapsed;
        }
        finally
        {
            _suppressPrefPersist = false;
        }
    }
}
