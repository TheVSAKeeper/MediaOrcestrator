using KeepShell.Automation;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap.Gallery;

internal sealed record GalleryOptions(GalleryArguments Arguments, string PluginPath)
{
    public const string Argument = "--gallery";
    public const string PluginsKey = "--plugins";

    private const string DefaultPluginFolder = "ModuleBuilds";

    public static IReadOnlyList<string> DefaultThemes { get; } = [AppThemes.LightKey, AppThemes.DarkKey, AppThemes.TarkovKey];

    public static GalleryOptions Parse(IEnumerable<string> args, string defaultDirectory)
    {
        var defaults = new GalleryDefaults
        {
            Directory = defaultDirectory,
            Themes = DefaultThemes,
        };

        var arguments = GalleryArguments.Parse(args, defaults, GalleryPages.Resolve, [PluginsKey]);
        var plugins = arguments.Extra(PluginsKey);

        return new(arguments, plugins.Length > 0
            ? Path.GetFullPath(plugins)
            : Path.Combine(AppContext.BaseDirectory, DefaultPluginFolder));
    }
}
