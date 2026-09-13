using MahApps.Metro.IconPacks;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public static class StatTones
{
    public const string Neutral = "Neutral";
    public const string Ok = "Ok";
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Muted = "Muted";
}

public sealed record StatBadge(PackIconLucideKind Icon, string Text, string Tone);

public sealed class SourceCardStats
{
    public static SourceCardStats Empty { get; } = new();

    public int Total { get; init; }
    public int Ok { get; init; }
    public int PartialOk { get; init; }
    public int Missing { get; init; }
    public int Error { get; init; }
    public int Skipped { get; init; }

    public IReadOnlyList<StatBadge> Badges
    {
        get
        {
            if (Total == 0)
            {
                return [new(PackIconLucideKind.Inbox, "пусто", StatTones.Muted)];
            }

            var list = new List<StatBadge>
            {
                new(PackIconLucideKind.Package, Total.ToString(), StatTones.Neutral),
            };

            if (Ok > 0)
            {
                list.Add(new(StatusIcons.Resolve(MediaStatus.Ok), Ok.ToString(), StatTones.Ok));
            }

            if (PartialOk > 0)
            {
                list.Add(new(StatusIcons.Resolve(MediaStatus.PartialOk), PartialOk.ToString(), StatTones.Warning));
            }

            if (Skipped > 0)
            {
                list.Add(new(StatusIcons.Resolve(MediaStatus.Skipped), Skipped.ToString(), StatTones.Muted));
            }

            if (Missing > 0)
            {
                list.Add(new(StatusIcons.Resolve(MediaStatus.Missing), Missing.ToString(), StatTones.Warning));
            }

            if (Error > 0)
            {
                list.Add(new(StatusIcons.Resolve(MediaStatus.Error), Error.ToString(), StatTones.Error));
            }

            return list;
        }
    }
}

public sealed class RelationCardStats
{
    public static RelationCardStats Empty { get; } = new();

    public int Transferred { get; init; }
    public int Pending { get; init; }
    public int Failed { get; init; }

    public bool HasAny => Transferred > 0 || Pending > 0 || Failed > 0;

    public IReadOnlyList<StatBadge> Badges
    {
        get
        {
            if (!HasAny)
            {
                return [new(PackIconLucideKind.Inbox, "нет медиа в источнике-доноре", StatTones.Muted)];
            }

            var list = new List<StatBadge>();

            if (Transferred > 0)
            {
                list.Add(new(StatusIcons.Resolve(MediaStatus.Ok), $"{Transferred} передано", StatTones.Ok));
            }

            if (Pending > 0)
            {
                list.Add(new(PackIconLucideKind.Clock, $"{Pending} ожидают", StatTones.Warning));
            }

            if (Failed > 0)
            {
                list.Add(new(StatusIcons.Resolve(MediaStatus.Error), $"{Failed} ошибок", StatTones.Error));
            }

            return list;
        }
    }
}
