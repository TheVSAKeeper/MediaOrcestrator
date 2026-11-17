using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed class MediaCellViewModel
{
    private string? _tooltip;

    public MediaCellViewModel(Source source, MediaSourceLink? link)
    {
        Source = source;
        Link = link;
        Status = TryGetStatus(link?.Status);
        Brush = ResolveBrush(Status);
        SortKey = ResolveSortKey(Link, Status);
        SourceSortNumber = link?.SortNumber ?? -1;
    }

    public Source Source { get; }

    public MediaSourceLink? Link { get; }

    public MediaStatus? Status { get; }

    public bool HasLink => Link is not null;

    public PackIconLucideKind Icon => StatusIcons.Resolve(Status?.Id);

    public Brush Brush { get; }

    public int SortKey { get; }

    public int SourceSortNumber { get; }

    public string Tooltip => _tooltip ??= Link is null
        ? $"{Source.Title}: нет в источнике"
        : $"{Source.Title}: {Status?.Text ?? Link.Status} ({Link.ExternalId})";

    private static MediaStatus? TryGetStatus(string? statusId)
    {
        if (string.IsNullOrWhiteSpace(statusId))
        {
            return null;
        }

        try
        {
            return MediaStatusHelper.GetById(statusId);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static Brush ResolveBrush(MediaStatus? status)
    {
        if (status is null)
        {
            return Brushes.Gray;
        }

        var c = status.IconColor;
        var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        brush.Freeze();

        return brush;
    }

    private static int ResolveSortKey(MediaSourceLink? link, MediaStatus? status)
    {
        if (link is null)
        {
            return 99;
        }

        return status?.Id switch
        {
            MediaStatus.Ok => 0,
            MediaStatus.PartialOk => 1,
            MediaStatus.Skipped => 2,
            MediaStatus.None => 3,
            MediaStatus.Missing => 4,
            MediaStatus.Error => 5,
            _ => 10,
        };
    }
}
