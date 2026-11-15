using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
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
    private static readonly TimeSpan ActiveSuccessDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ActiveErrorDuration = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan WriteFailedThrottle = TimeSpan.FromSeconds(30);

    private readonly ActionHolder _actions;
    private readonly ISettingsStore _settings;
    private readonly ShellPreferences _preferences;
    private readonly ToastHostViewModel _toasts;
    private readonly IShellLauncher _shellLauncher;
    private readonly IServiceProvider _services;
    private readonly ILogger<ToastNotificationService> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _seen = new();
    private DateTime _lastWriteFailedUtc;
    private string? _lastWriteFailedReason;

    public ToastNotificationService(
        ActionHolder actions,
        ISettingsStore settings,
        ShellPreferences preferences,
        ToastHostViewModel toasts,
        IShellLauncher shellLauncher,
        IServiceProvider services,
        ILogger<ToastNotificationService> logger)
    {
        _actions = actions;
        _settings = settings;
        _preferences = preferences;
        _toasts = toasts;
        _shellLauncher = shellLauncher;
        _services = services;
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

        var isFailed = action.State == ActionState.Failed;
        var headline = isFailed ? "Ошибка задачи" : "Задача завершена";
        var isActive = IsMainWindowActive();

        if (isActive)
        {
            var message = BuildInAppActionMessage(action, isFailed);
            var severity = isFailed ? StatusSeverity.Error : StatusSeverity.Success;
            var duration = isFailed ? ActiveErrorDuration : ActiveSuccessDuration;
            _toasts.Show(message, "К задачам", OpenTasksDrawer, severity, duration);

            if (OnlyWhenInactive())
            {
                return;
            }
        }

        ShowUwpToast(headline, action.Name, action.Status, action.Id.ToString("N"), $"задачи {action.Id}");
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

        dispatcher.BeginInvoke(() => ShowWriteFailed(reason));
    }

    private void ShowWriteFailed(string? reason)
    {
        var now = DateTime.UtcNow;

        if (now - _lastWriteFailedUtc < WriteFailedThrottle
            && string.Equals(_lastWriteFailedReason, reason, StringComparison.Ordinal))
        {
            return;
        }

        _lastWriteFailedUtc = now;
        _lastWriteFailedReason = reason;

        if (IsMainWindowActive())
        {
            var message = string.IsNullOrWhiteSpace(reason)
                ? "Файл settings.toml не удалось записать – последние изменения пропадут после перезапуска."
                : $"Файл settings.toml не удалось записать: {reason}";

            _toasts.Show(message, "Показать файл", RevealSettingsFile, StatusSeverity.Error, ActiveErrorDuration);
            return;
        }

        ShowUwpToast(
            "Настройки не сохранились",
            "Файл settings.toml не удалось записать, последние изменения пропадут после перезапуска.",
            reason,
            "settings-write-failed",
            "отказа записи настроек");
    }

    private void OpenTasksDrawer()
    {
        var shell = _services.GetService<ShellViewModel>();

        if (shell is not null)
        {
            shell.IsTasksDrawerOpen = true;
        }
    }

    private void RevealSettingsFile()
    {
        _shellLauncher.Reveal(_settings.FilePath);
    }

    private static string BuildInAppActionMessage(ActionHolder.RunningAction action, bool isFailed)
    {
        if (!isFailed)
        {
            return $"Готово: {action.Name}";
        }

        var status = action.Status?.Trim();

        if (string.IsNullOrWhiteSpace(status))
        {
            return $"Ошибка: {action.Name}";
        }

        if (status.Length > MaxReasonLength)
        {
            status = string.Concat(status.AsSpan(0, MaxReasonLength), "…");
        }

        return $"Ошибка ({action.Name}): {status}";
    }

    private void ShowUwpToast(string headline, string body, string? attribution, string tag, string what)
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
        return window is { IsVisible: true, IsActive: true } && window.WindowState != WindowState.Minimized;
    }

    private bool IsEnabled() => _preferences.EnableToastNotifications;

    private bool OnlyWhenInactive() => _preferences.ToastOnlyWhenInactive;
}
