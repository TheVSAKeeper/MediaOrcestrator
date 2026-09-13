using MahApps.Metro.IconPacks;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

internal static class StatusIcons
{
    public static PackIconLucideKind Resolve(string? statusId) => statusId switch
    {
        MediaStatus.Ok => PackIconLucideKind.CircleCheck,
        MediaStatus.PartialOk => PackIconLucideKind.CircleAlert,
        MediaStatus.Skipped => PackIconLucideKind.CircleSlash,
        MediaStatus.None => PackIconLucideKind.Circle,
        MediaStatus.Missing => PackIconLucideKind.CircleOff,
        MediaStatus.Error => PackIconLucideKind.CircleX,
        _ => PackIconLucideKind.Dot,
    };
}
