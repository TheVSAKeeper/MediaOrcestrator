using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class AppUpdateViewModel : ObservableObject
{
    private const string SkippedVersionKey = "wpf.update.skipped_version";

    private readonly AppUpdateManager _updateManager;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly ILogger<AppUpdateViewModel> _logger;
    private readonly IShellLauncher _shellLauncher;
    private readonly IUiDispatcher _dispatcher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingUpdate))]
    [NotifyPropertyChangedFor(nameof(VersionLabel))]
    [NotifyCanExecuteChangedFor(nameof(ShowDetailsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DismissCommand))]
    private AppUpdateInfo? _pendingUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCheckStatus))]
    private string? _checkStatus;

    [ObservableProperty]
    private bool _isChecking;

    public AppUpdateViewModel(
        AppUpdateManager updateManager,
        IDialogService dialogs,
        ISettingsStore settings,
        ILogger<AppUpdateViewModel> logger,
        IShellLauncher shellLauncher,
        IUiDispatcher? dispatcher = null)
    {
        _updateManager = updateManager;
        _dialogs = dialogs;
        _settings = settings;
        _logger = logger;
        _shellLauncher = shellLauncher;
        _dispatcher = dispatcher ?? new UiDispatcher();
    }

    public bool HasPendingUpdate => PendingUpdate is not null;

    public string VersionLabel => PendingUpdate is null
        ? string.Empty
        : $"Доступна версия {PendingUpdate.Version}";

    public string CurrentVersionText => _updateManager.CurrentVersion.ToString();

    public bool HasCheckStatus => !string.IsNullOrEmpty(CheckStatus);

    [RelayCommand]
    private async Task CheckNowAsync(CancellationToken token)
    {
        IsChecking = true;
        CheckStatus = "Спрашиваем GitHub…";

        try
        {
            var update = await _updateManager.CheckForUpdateAsync(token);

            if (_updateManager.LastCheckError is { } error)
            {
                CheckStatus = $"Проверить не удалось: {error}";
                return;
            }

            if (update is null)
            {
                SetPendingUpdate(null);
                CheckStatus = "Обновлений нет – установлена последняя версия.";
                return;
            }

            _settings.SetValue(SkippedVersionKey, string.Empty);
            SetPendingUpdate(update);
            CheckStatus = $"Доступна версия {update.Version}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            CheckStatus = null;
        }
        finally
        {
            IsChecking = false;
        }
    }

    public void SetPendingUpdate(AppUpdateInfo? update)
    {
        if (update is null)
        {
            PendingUpdate = null;
            return;
        }

        var skipped = _settings.GetStringValue(SkippedVersionKey);

        if (!string.IsNullOrWhiteSpace(skipped)
            && string.Equals(skipped, update.Version, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Версия {Version} помечена пользователем как пропущенная – баннер скрыт", update.Version);
            PendingUpdate = null;
            return;
        }

        _dispatcher.Invoke(() => PendingUpdate = update);
    }

    [RelayCommand(CanExecute = nameof(HasPendingUpdate))]
    private async Task ShowDetailsAsync()
    {
        var update = PendingUpdate;

        if (update is null)
        {
            return;
        }

        var dialog = new AppUpdateDialogViewModel(_updateManager, update, _logger, _shellLauncher);

        try
        {
            await _dialogs.ShowAsync(dialog);

            if (dialog.SkipRequested)
            {
                _settings.SetValue(SkippedVersionKey, update.Version);
                _logger.LogInformation("Версия {Version} пропущена пользователем", update.Version);
                PendingUpdate = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при показе диалога обновления");
            _dialogs.Error("Обновление", "Не удалось открыть окно обновления: " + ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasPendingUpdate))]
    private void Dismiss()
    {
        var update = PendingUpdate;

        if (update is null)
        {
            return;
        }

        _settings.SetValue(SkippedVersionKey, update.Version);
        _logger.LogInformation("Баннер обновления скрыт; версия {Version} запомнена как пропущенная", update.Version);
        PendingUpdate = null;
    }
}
