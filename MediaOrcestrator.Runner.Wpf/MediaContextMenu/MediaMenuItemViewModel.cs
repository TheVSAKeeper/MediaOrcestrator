using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.MediaContextMenu;

public sealed class MediaMenuItemViewModel
{
    public required string Text { get; init; }

    public PackIconLucideKind? Icon { get; init; }

    public PackIconLucideKind IconOrDefault => Icon ?? PackIconLucideKind.Dot;

    public Brush? GlyphBrush { get; init; }

    public string? Tooltip { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsSeparator { get; init; }

    public bool IsTransferAction { get; init; }

    public ICommand? Command { get; init; }

    public ObservableCollection<MediaMenuItemViewModel>? Children { get; init; }

    public bool HasChildren => Children is { Count: > 0 };

    public bool IsLeaf => !IsSeparator && !HasChildren;

    public static MediaMenuItemViewModel Separator() => new()
    {
        Text = string.Empty,
        IsSeparator = true,
        IsEnabled = false,
    };

    public static MediaMenuItemViewModel FromSpec(MenuItemSpec spec, ILogger? logger = null)
    {
        ICommand? command = null;

        if (spec.Execute is { } execute && spec.Enabled)
        {
            command = new AsyncRelayCommand(async () =>
            {
                try
                {
                    await execute();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Ошибка выполнения пункта меню '{Text}'", spec.Text);
                }
            });
        }

        ObservableCollection<MediaMenuItemViewModel>? children = null;

        if (spec.Children is { Count: > 0 })
        {
            children = new(spec.Children.Select(c => FromSpec(c, logger)));
        }

        return new()
        {
            Text = spec.Text,
            Icon = spec.Glyph?.Icon,
            GlyphBrush = spec.Glyph?.Brush,
            Tooltip = spec.Tooltip,
            IsEnabled = spec.Enabled,
            IsTransferAction = spec.IsTransferAction,
            Command = command,
            Children = children,
        };
    }
}
