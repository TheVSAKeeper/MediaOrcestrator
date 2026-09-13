using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Windows;

namespace MediaOrcestrator.Runner.Wpf.MediaContextMenu;

public sealed class MediaContextMenuBuilder : IDisposable
{
    private readonly IReadOnlyList<IMediaMenuAction> _actions;
    private readonly ILogger<MediaContextMenuBuilder> _logger;

    private CancellationTokenSource? _menuCts;

    public MediaContextMenuBuilder(IEnumerable<IMediaMenuAction> actions, ILogger<MediaContextMenuBuilder> logger)
    {
        _actions = actions.OrderBy(a => a.Order).ToList();
        _logger = logger;
    }

    public ObservableCollection<MediaMenuItemViewModel> Build(MediaSelection selection, MediaActionContext ctx, bool includeAsync = true)
    {
        var token = CancellationToken.None;

        if (includeAsync)
        {
            _menuCts?.Cancel();
            _menuCts?.Dispose();
            _menuCts = new();
            token = _menuCts.Token;
        }

        var items = new ObservableCollection<MediaMenuItemViewModel>();

        if (selection.IsBatch)
        {
            items.Add(new()
            {
                Text = $"Выбрано: {selection.Count} медиа",
                IsEnabled = false,
            });

            items.Add(MediaMenuItemViewModel.Separator());
        }

        var addedAnything = false;
        var asyncSlots = new List<AsyncSlot>();

        foreach (var action in _actions)
        {
            List<MenuItemSpec> specs;

            try
            {
                specs = action.Build(selection, ctx).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка построения пунктов меню для {Action}", action.GetType().Name);
                continue;
            }

            var wantsAsync = includeAsync && action is IAsyncMediaMenuAction;

            MediaMenuItemViewModel? separator = null;

            if (addedAnything && (specs.Count > 0 || wantsAsync))
            {
                separator = MediaMenuItemViewModel.Separator();
                items.Add(separator);
            }

            foreach (var spec in specs)
            {
                items.Add(MediaMenuItemViewModel.FromSpec(spec, _logger));
                addedAnything = true;
            }

            if (wantsAsync && action is IAsyncMediaMenuAction asyncAction)
            {
                var placeholder = new MediaMenuItemViewModel
                {
                    Text = asyncAction.LoadingPlaceholder,
                    Icon = asyncAction.LoadingIcon?.Icon,
                    GlyphBrush = asyncAction.LoadingIcon?.Brush,
                    IsEnabled = false,
                };

                items.Add(placeholder);
                asyncSlots.Add(new(asyncAction, placeholder, separator));
                addedAnything = true;
            }
        }

        if (asyncSlots.Count > 0)
        {
            _ = LoadAsyncSlotsAsync(items, asyncSlots, selection, ctx, token);
        }

        return items;
    }

    public void Dispose()
    {
        _menuCts?.Cancel();
        _menuCts?.Dispose();
        _menuCts = null;
    }

    private async Task LoadAsyncSlotsAsync(
        ObservableCollection<MediaMenuItemViewModel> items,
        IReadOnlyList<AsyncSlot> slots,
        MediaSelection selection,
        MediaActionContext ctx,
        CancellationToken token)
    {
        foreach (var slot in slots)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var asyncItems = (await slot.Action.BuildAsync(selection, ctx, token)).ToList();

                if (token.IsCancellationRequested)
                {
                    return;
                }

                Dispatch(() => ReplacePlaceholder(items, slot, asyncItems));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка загрузки async-пунктов меню для {Action}", slot.Action.GetType().Name);

                Dispatch(() =>
                {
                    var index = items.IndexOf(slot.Placeholder);

                    if (index < 0)
                    {
                        return;
                    }

                    items[index] = new()
                    {
                        Text = "Ошибка загрузки",
                        IsEnabled = false,
                    };
                });
            }
        }
    }

    private void ReplacePlaceholder(
        ObservableCollection<MediaMenuItemViewModel> items,
        AsyncSlot slot,
        IReadOnlyList<MenuItemSpec> asyncSpecs)
    {
        var idx = items.IndexOf(slot.Placeholder);

        if (idx < 0)
        {
            return;
        }

        items.RemoveAt(idx);

        if (asyncSpecs.Count == 0)
        {
            if (slot.Separator != null && items.Contains(slot.Separator))
            {
                items.Remove(slot.Separator);
            }

            return;
        }

        foreach (var spec in asyncSpecs)
        {
            items.Insert(idx, MediaMenuItemViewModel.FromSpec(spec, _logger));
            idx++;
        }
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    private sealed record AsyncSlot(IAsyncMediaMenuAction Action, MediaMenuItemViewModel Placeholder, MediaMenuItemViewModel? Separator);
}
