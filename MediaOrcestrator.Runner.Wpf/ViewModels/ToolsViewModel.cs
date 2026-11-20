using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class ToolsViewModel : ObservableObject
{
    private readonly ToolManager _toolManager;
    private readonly ActionHolder _actionHolder;
    private readonly ILogger<ToolsViewModel> _logger;
    private readonly IShellLauncher _shellLauncher;
    private readonly IClipboardService _clipboard;
    private CancellationTokenSource? _cts;
    private bool _initialChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(CanUpdateAll))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdateAll))]
    private bool _hasPendingUpdates;

    [ObservableProperty]
    private bool _isChecking;

    public bool IsNotBusy => !IsBusy;

    public bool CanUpdateAll => IsNotBusy && HasPendingUpdates;

    public ToolsViewModel(
        ToolManager toolManager,
        ActionHolder actionHolder,
        ILogger<ToolsViewModel> logger,
        IShellLauncher shellLauncher,
        IClipboardService clipboard)
    {
        _toolManager = toolManager;
        _actionHolder = actionHolder;
        _logger = logger;
        _shellLauncher = shellLauncher;
        _clipboard = clipboard;
        Items = [];
        LoadCurrentState();
    }

    public ObservableCollection<ToolItemViewModel> Items { get; }

    internal void SuppressAutoCheckForAutomation() => _initialChecked = true;

    public async Task EnsureCheckedAsync()
    {
        if (_initialChecked)
        {
            return;
        }

        _initialChecked = true;
        await CheckUpdatesAsync();
    }

    [RelayCommand]
    private Task CheckUpdates()
    {
        return CheckUpdatesAsync();
    }

    [RelayCommand]
    private async Task UpdateAll()
    {
        if (IsBusy)
        {
            return;
        }

        var pending = Items.Where(i => i.HasAction).ToList();

        if (pending.Count == 0)
        {
            return;
        }

        await ResetCts();
        var token = _cts!.Token;

        IsBusy = true;
        var done = 0;
        StatusMessage = $"Обновление 0/{pending.Count}";

        var bulkCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var bulkToken = bulkCts.Token;
        var bulkAction = _actionHolder.Register(
            $"Обновление инструментов ({pending.Count})",
            "Старт…",
            pending.Count,
            bulkCts,
            kind: ActionKind.Download);

        try
        {
            using (_actionHolder.BeginScope(bulkAction))
            {
                foreach (var current in pending)
                {
                    bulkToken.ThrowIfCancellationRequested();

                    using var childCts = CancellationTokenSource.CreateLinkedTokenSource(bulkToken);
                    var childToken = childCts.Token;
                    var childAction = _actionHolder.Register(
                        $"Обновление {current.Name}",
                        "Загрузка…",
                        100,
                        childCts,
                        kind: ActionKind.Download);

                    current.IsBusy = true;
                    current.Progress = 0;
                    var stepLabel = $"{current.Name} ({done + 1}/{pending.Count})";
                    StatusMessage = $"Обновление {stepLabel}…";
                    bulkAction.Status = stepLabel;

                    var progress = new Progress<double>(p =>
                    {
                        var pct = Math.Clamp(p * 100, 0, 100);
                        current.Progress = pct;
                        childAction.SetProgress((int)Math.Round(pct));
                    });

                    try
                    {
                        await Task.Run(() => _toolManager.UpdateToolAsync(current.Name, progress, childToken), childToken);
                        childAction.Finish("Готово");
                    }
                    catch (OperationCanceledException) when (childToken.IsCancellationRequested)
                    {
                        childAction.MarkCancelled("Отменено");
                        bulkCts.Cancel();
                        throw;
                    }
                    catch (Exception ex)
                    {
                        childAction.Fail("Ошибка: " + ex.Message, ex);
                        throw;
                    }
                    finally
                    {
                        current.IsBusy = false;
                        current.Progress = 0;
                    }

                    done++;
                    bulkAction.ProgressPlus();
                }
            }

            var statuses = await Task.Run(() => _toolManager.CheckForUpdatesAsync(bulkToken), bulkToken);
            Apply(statuses);
            StatusMessage = $"Готово: обновлено {done}";
            bulkAction.Finish($"Обновлено {done}");
        }
        catch (OperationCanceledException) when (bulkToken.IsCancellationRequested)
        {
            StatusMessage = $"Обновление отменено ({done}/{pending.Count})";
            bulkAction.MarkCancelled($"Отменено: {done}/{pending.Count}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось обновить инструменты пакетом");
            StatusMessage = $"Ошибка: {ex.Message}";
            bulkAction.Fail("Ошибка: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpdateTool(ToolItemViewModel? item)
    {
        if (item is null || !item.HasAction || IsBusy)
        {
            return;
        }

        await ResetCts();
        var token = _cts!.Token;

        IsBusy = true;
        item.IsBusy = true;
        item.Progress = 0;
        StatusMessage = $"Обновление {item.Name}… 0 %";

        var actionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var actionToken = actionCts.Token;
        var action = _actionHolder.Register(
            $"Обновление {item.Name}",
            "Загрузка…",
            100,
            actionCts,
            kind: ActionKind.Download);

        var progress = new Progress<double>(p =>
        {
            var clamped = Math.Clamp(p * 100, 0, 100);
            item.Progress = clamped;
            var pct = (int)Math.Round(clamped);
            StatusMessage = $"Обновление {item.Name}… {pct} %";
            action.SetProgress(pct);
            action.Status = $"{pct} %";
        });

        try
        {
            await Task.Run(() => _toolManager.UpdateToolAsync(item.Name, progress, actionToken), actionToken);
            StatusMessage = $"{item.Name}: обновлено";
            action.Finish("Готово");

            var statuses = await Task.Run(() => _toolManager.CheckForUpdatesAsync(token), token);
            Apply(statuses);
        }
        catch (OperationCanceledException) when (actionToken.IsCancellationRequested || token.IsCancellationRequested)
        {
            StatusMessage = $"Обновление {item.Name} отменено";
            action.MarkCancelled("Отменено");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось обновить инструмент {Tool}", item.Name);
            StatusMessage = $"Ошибка: {ex.Message}";
            action.Fail("Ошибка: " + ex.Message, ex);
        }
        finally
        {
            item.IsBusy = false;
            item.Progress = 0;
            IsBusy = false;
        }
    }

    private async Task CheckUpdatesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await ResetCts();
        var token = _cts!.Token;

        IsBusy = true;
        IsChecking = true;
        StatusMessage = "Проверка обновлений…";

        var actionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var actionToken = actionCts.Token;
        var action = _actionHolder.Register(
            "Проверка обновлений инструментов",
            "Запрос GitHub…",
            0,
            actionCts,
            kind: ActionKind.Other);

        try
        {
            var statuses = await Task.Run(() => _toolManager.CheckForUpdatesAsync(actionToken), actionToken);
            actionToken.ThrowIfCancellationRequested();
            Apply(statuses);
            StatusMessage = $"Проверено: {DateTime.Now:HH:mm:ss}";
            action.Finish($"Проверено: {statuses.Count}");
        }
        catch (OperationCanceledException) when (actionToken.IsCancellationRequested)
        {
            StatusMessage = "Проверка отменена";
            action.MarkCancelled("Отменено");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при проверке инструментов");
            StatusMessage = $"Ошибка: {ex.Message}";
            action.Fail("Ошибка: " + ex.Message, ex);
        }
        finally
        {
            IsChecking = false;
            IsBusy = false;
        }
    }

    private void LoadCurrentState()
    {
        Items.Clear();

        foreach (var (name, _) in _toolManager.GetRegistry().OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var path = _toolManager.GetToolPath(name);
            Items.Add(new(name, path, _shellLauncher, _clipboard));
        }

        HasPendingUpdates = Items.Any(i => i.HasAction);
    }

    private void Apply(IReadOnlyList<ToolStatus> statuses)
    {
        var byName = statuses.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var item in Items)
        {
            if (!byName.TryGetValue(item.Name, out var status))
            {
                continue;
            }

            item.InstalledVersion = status.InstalledVersion ?? "–";
            item.LatestVersion = status.LatestVersion ?? "–";
            item.ResolvedPath = status.ResolvedPath;
            item.State = status switch
            {
                { InstalledVersion: null } => ToolState.NotInstalled,
                { UpdateAvailable: true } => ToolState.UpdateAvailable,
                _ => ToolState.UpToDate,
            };
        }

        HasPendingUpdates = Items.Any(i => i.HasAction);
    }

    private async Task ResetCts()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }

        _cts = new();
    }
}
