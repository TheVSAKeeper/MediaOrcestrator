using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Collections.Concurrent;
using System.Windows;

namespace MediaOrcestrator.Runner.Wpf.Services;

internal sealed class ToastNotificationService : IHostedService
{
    private const int MaxReasonLength = 120;
    private static readonly TimeSpan MinDurationForSuccess = TimeSpan.FromSeconds(5);

    private readonly ActionHolder _actions;
    private readonly ISettingsStore _settings;
    private readonly ShellPreferences _preferences;
    private readonly ILogger<ToastNotificationService> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _seen = new();

    public ToastNotificationService(
        ActionHolder actions,
        ISettingsStore settings,
        ShellPreferences preferences,
        ILogger<ToastNotificationService> logger)
    {
        _actions = actions;
        _settings = settings;
        _preferences = preferences;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var action in _actions.CompletedSnapshot())
        {
            _seen.TryAdd(action.Id, 1);
        }

        _actions.Changed += OnActionsChanged;
        _settings.WriteFailed += OnSettingsWriteFailed;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _actions.Changed -= OnActionsChanged;
        _settings.WriteFailed -= OnSettingsWriteFailed;
        return Task.CompletedTask;
    }

    private void OnActionsChanged(object? sender, EventArgs e)
    {
        if (!IsEnabled())
        {
            return;
        }

        var newly = new List<ActionHolder.RunningAction>();

        foreach (var action in _actions.CompletedSnapshot())
        {
            if (_seen.TryAdd(action.Id, 1))
            {
                newly.Add(action);
            }
        }

        if (newly.Count == 0)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            foreach (var action in newly)
            {
                TryShowToast(action);
            }
        });
    }

    private void TryShowToast(ActionHolder.RunningAction action)
    {
        if (action.State == ActionState.Cancelled)
        {
            return;
        }

        if (action.State == ActionState.Succeeded && action.Duration < MinDurationForSuccess)
        {
            return;
        }

        if (OnlyWhenInactive() && IsMainWindowActive())
        {
            return;
        }

        var headline = action.State == ActionState.Failed ? "Ошибка задачи" : "Задача завершена";

        ShowToast(headline, action.Name, action.Status, action.Id.ToString("N"), $"задачи {action.Id}");
    }

    private void OnSettingsWriteFailed(object? sender, SettingsWriteFailedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        var reason = e.Exception.Message?.Trim();

        if (reason is { Length: > MaxReasonLength })
        {
            reason = null;
        }

        dispatcher.BeginInvoke(() => ShowToast(
            "Настройки не сохранились",
            "Файл settings.toml не удалось записать, последние изменения пропадут после перезапуска.",
            reason,
            "settings-write-failed",
            "отказа записи настроек"));
    }

    private void ShowToast(string headline, string body, string? attribution, string tag, string what)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddText(headline)
                .AddText(body);

            if (!string.IsNullOrWhiteSpace(attribution))
            {
                builder.AddAttributionText(attribution);
            }

            builder.Show(toast =>
            {
                toast.ExpirationTime = DateTime.Now.AddMinutes(30);
                toast.Tag = tag;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось показать toast для {What}", what);
        }
    }

    private static bool IsMainWindowActive()
    {
        var window = Application.Current?.MainWindow;
        return window is not null && window.IsActive;
    }

    private bool IsEnabled() => _preferences.EnableToastNotifications;

    private bool OnlyWhenInactive() => _preferences.ToastOnlyWhenInactive;
}
