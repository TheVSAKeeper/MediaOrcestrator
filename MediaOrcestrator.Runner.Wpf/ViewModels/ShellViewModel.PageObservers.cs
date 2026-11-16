using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class ShellViewModel
{
    private void AttachRenderMetricSource(IPageRenderMetric? next)
    {
        if (_renderMetricSource is not null)
        {
            _renderMetricSource.PropertyChanged -= OnRenderMetricSourcePropertyChanged;
        }

        _renderMetricSource = next;
        _customRenderText = next?.RenderDurationText;
        _customRenderStages = next?.RenderStages;

        if (next is not null)
        {
            next.PropertyChanged += OnRenderMetricSourcePropertyChanged;
        }
    }

    private void OnRenderMetricSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;

        if (name is not (null or ""
            or nameof(IPageRenderMetric.RenderDurationText)
            or nameof(IPageRenderMetric.RenderStages)))
        {
            return;
        }

        var source = _renderMetricSource;

        if (source is null)
        {
            return;
        }

        _dispatcher.Invoke(() =>
        {
            if (!ReferenceEquals(_renderMetricSource, source))
            {
                return;
            }

            _customRenderText = source.RenderDurationText;
            _customRenderStages = source.RenderStages;
            UpdatePageRenderDurationText();
            LogRenderMetric(source, name);
        });
    }

    private void LogRenderMetric(IPageRenderMetric source, string? changedProperty)
    {
        if (changedProperty != nameof(IPageRenderMetric.RenderStages))
        {
            return;
        }

        var stages = source.RenderStages;

        if (stages is null || stages.Count == 0)
        {
            return;
        }

        var pageName = (source as IPageHeader)?.PageTitle ?? source.GetType().Name;
        var stagesText = string.Join("; ", stages.Select(s => $"{s.Name}={s.Value}"));

        _logger.LogInformation(
            "Отрисовка страницы «{Page}»: {Duration}, стадии: {Stages}",
            pageName,
            source.RenderDurationText ?? "–",
            stagesText);
    }

    private void BeginMeasurePageRender()
    {
        _defaultRenderText = null;
        UpdatePageRenderDurationText();

        var selectedAtStart = Selected;

        _renderProbe.MeasureIdle(elapsed =>
        {
            if (!ReferenceEquals(Selected, selectedAtStart))
            {
                return;
            }

            _defaultRenderText = FormatRenderDuration(elapsed);
            UpdatePageRenderDurationText();
        });
    }

    private void UpdatePageRenderDurationText()
    {
        var hasCustom = !string.IsNullOrWhiteSpace(_customRenderText);
        PageRenderDurationText = hasCustom ? _customRenderText : _defaultRenderText;
        PageRenderStages = hasCustom ? _customRenderStages : null;
    }

    private static string FormatRenderDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds < 1000)
        {
            return $"{elapsed.TotalMilliseconds:F0} мс";
        }

        return $"{elapsed.TotalSeconds:F2} с";
    }

    [RelayCommand]
    private void ToggleTasksDrawer()
    {
        IsTasksDrawerOpen = !IsTasksDrawerOpen;
    }

    [RelayCommand(CanExecute = nameof(CanCloseTasksDrawer))]
    private void CloseTasksDrawer()
    {
        IsTasksDrawerOpen = false;
    }

    private bool CanCloseTasksDrawer() => IsTasksDrawerOpen && !Modal.HasActive;

    [RelayCommand(CanExecute = nameof(CanHandleEscape))]
    private void HandleEscape()
    {
        if (Modal.HasActive)
        {
            return;
        }

        if (IsTasksDrawerOpen)
        {
            IsTasksDrawerOpen = false;
            return;
        }

        if (CurrentPageStatus is { IsBusy: true, CancelCommand: { } cancel } && cancel.CanExecute(null))
        {
            cancel.Execute(null);
            return;
        }

        if (Selected?.Content is IPageEscapeHandler handler)
        {
            handler.TryHandleEscape();
        }
    }

    private bool CanHandleEscape()
    {
        if (Modal.HasActive)
        {
            return false;
        }

        if (IsTasksDrawerOpen)
        {
            return true;
        }

        if (CurrentPageStatus is { IsBusy: true, CancelCommand: { } cancel } && cancel.CanExecute(null))
        {
            return true;
        }

        return (Selected?.Content as IPageEscapeHandler)?.CanHandleEscape == true;
    }

    [RelayCommand(CanExecute = nameof(CanFocusPageSearch))]
    private void FocusPageSearch()
    {
        if (Selected?.Content is IPageSearchable searchable)
        {
            searchable.RequestSearchFocus();
        }
    }

    private bool CanFocusPageSearch() => !Modal.HasActive && !IsTasksDrawerOpen && Selected?.Content is IPageSearchable;

    [RelayCommand(CanExecute = nameof(CanRefreshActivePage))]
    private void RefreshActivePage()
    {
        if (PageRefreshCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private bool CanRefreshActivePage() => !Modal.HasActive && PageRefreshCommand?.CanExecute(null) == true;

    private void OnModalPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModalHostViewModel.HasActive))
        {
            CloseTasksDrawerCommand.NotifyCanExecuteChanged();
            HandleEscapeCommand.NotifyCanExecuteChanged();
            FocusPageSearchCommand.NotifyCanExecuteChanged();
            RefreshActivePageCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(PageRefreshCommand))
        {
            AttachPageRefreshCommand(PageRefreshCommand);
        }

        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(CurrentPageStatus))
        {
            AttachPageStatus(CurrentPageStatus);
        }
    }

    private void AttachPageStatus(IPageStatus? next)
    {
        if (_observedPageStatus is INotifyPropertyChanged oldObservable)
        {
            oldObservable.PropertyChanged -= OnObservedPageStatusPropertyChanged;
        }

        _observedPageStatus = next;

        if (next is INotifyPropertyChanged newObservable)
        {
            newObservable.PropertyChanged += OnObservedPageStatusPropertyChanged;
        }

        AttachPageCancelCommand(next?.CancelCommand);
        HandleEscapeCommand.NotifyCanExecuteChanged();
    }

    private void OnObservedPageStatusPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(IPageStatus.IsBusy) or nameof(IPageStatus.CancelCommand))
        {
            AttachPageCancelCommand(_observedPageStatus?.CancelCommand);
            HandleEscapeCommand.NotifyCanExecuteChanged();
        }
    }

    private void AttachPageCancelCommand(ICommand? next)
    {
        if (ReferenceEquals(_observedPageCancelCommand, next))
        {
            return;
        }

        if (_observedPageCancelCommand is not null)
        {
            _observedPageCancelCommand.CanExecuteChanged -= OnObservedPageCancelCanExecuteChanged;
        }

        _observedPageCancelCommand = next;

        if (next is not null)
        {
            next.CanExecuteChanged += OnObservedPageCancelCanExecuteChanged;
        }
    }

    private void OnObservedPageCancelCanExecuteChanged(object? sender, EventArgs e)
    {
        HandleEscapeCommand.NotifyCanExecuteChanged();
    }

    private void AttachEscapeHandler(IPageEscapeHandler? handler)
    {
        if (_observedEscapeHandler is not null)
        {
            _observedEscapeHandler.PropertyChanged -= OnObservedEscapeHandlerPropertyChanged;
        }

        _observedEscapeHandler = handler as INotifyPropertyChanged;

        if (_observedEscapeHandler is not null)
        {
            _observedEscapeHandler.PropertyChanged += OnObservedEscapeHandlerPropertyChanged;
        }

        HandleEscapeCommand.NotifyCanExecuteChanged();
    }

    private void OnObservedEscapeHandlerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(IPageEscapeHandler.CanHandleEscape))
        {
            HandleEscapeCommand.NotifyCanExecuteChanged();
        }
    }

    private void AttachPageRefreshCommand(ICommand? next)
    {
        if (_observedPageRefreshCommand is not null)
        {
            _observedPageRefreshCommand.CanExecuteChanged -= OnObservedPageRefreshCanExecuteChanged;
        }

        _observedPageRefreshCommand = next;

        if (next is not null)
        {
            next.CanExecuteChanged += OnObservedPageRefreshCanExecuteChanged;
        }

        RefreshActivePageCommand.NotifyCanExecuteChanged();
    }

    private void OnObservedPageRefreshCanExecuteChanged(object? sender, EventArgs e)
    {
        RefreshActivePageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsTasksDrawerOpenChanged(bool value)
    {
        CloseTasksDrawerCommand.NotifyCanExecuteChanged();
        HandleEscapeCommand.NotifyCanExecuteChanged();
        FocusPageSearchCommand.NotifyCanExecuteChanged();

        if (_suppressPersist)
        {
            return;
        }

        _settings.SetValue(TasksDrawerOpenKey, value ? "true" : "false");
    }
}
