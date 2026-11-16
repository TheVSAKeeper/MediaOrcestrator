using KeepShell.Automation;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap.Gallery;

internal sealed record GalleryOptions(
    GalleryArguments Arguments,
    string PluginPath,
    IReadOnlyList<string> Dialogs,
    string State)
{
    public const string Argument = "--gallery";
    public const string PluginsKey = "--plugins";
    public const string DialogsKey = "--dialogs";
    public const string StateKey = "--state";
    public const string NoneValue = "none";
    public const string AllValue = "all";

    private const string DefaultPluginFolder = "ModuleBuilds";

    public string Directory => Arguments.Directory;

    public IReadOnlyList<string> Pages => Arguments.Pages;

    public IReadOnlyList<string> Themes => Arguments.Themes;

    public static IReadOnlyList<string> DefaultThemes { get; } = [AppThemes.LightKey, AppThemes.DarkKey, AppThemes.TarkovKey];

    public static GalleryOptions Parse(IEnumerable<string> args, string defaultDirectory)
    {
        var defaults = new GalleryDefaults
        {
            Directory = defaultDirectory,
            Themes = DefaultThemes,
        };

        var arguments = GalleryArguments.Parse(args, defaults, GalleryPages.Resolve, [PluginsKey, DialogsKey, StateKey]);
        var unknown = new List<string>();
        var dialogs = ParseSet(arguments.Extra(DialogsKey), GalleryDialogs.All, unknown);
        var state = ParseState(arguments.Extra(StateKey), unknown);

        if (unknown.Count > 0)
        {
            arguments = arguments with
            {
                Unknown = [.. arguments.Unknown, .. unknown],
            };
        }

        var plugins = arguments.Extra(PluginsKey);
        var pluginPath = plugins.Length > 0
            ? Path.GetFullPath(plugins)
            : Path.Combine(AppContext.BaseDirectory, DefaultPluginFolder);

        return new(arguments, pluginPath, dialogs, state);
    }

    private static IReadOnlyList<string> ParseSet(string value, IReadOnlyList<string> all, List<string> unknown)
    {
        var requested = GalleryArguments.Split(value);

        if (requested.Count == 1 && string.Equals(requested[0], NoneValue, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (requested.Count == 1 && string.Equals(requested[0], AllValue, StringComparison.OrdinalIgnoreCase))
        {
            return all;
        }

        var chosen = new List<string>();

        foreach (var item in requested)
        {
            var match = all.FirstOrDefault(known => string.Equals(known, item, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                unknown.Add(item);
                continue;
            }

            if (!chosen.Contains(match, StringComparer.Ordinal))
            {
                chosen.Add(match);
            }
        }

        return chosen.Count > 0 ? chosen : all;
    }

    private static string ParseState(string value, List<string> unknown)
    {
        if (GalleryStates.Match(value) is { } known)
        {
            return known;
        }

        if (value.Length > 0)
        {
            unknown.Add(value);
        }

        return GalleryStates.Idle;
    }
}
