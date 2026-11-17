using System.Globalization;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

internal static class StartupCommandLine
{
    public const string NavCommandPrefix = "nav:";
    public const string DrawerCommandPrefix = "drawer:";
    public const string ApplyImportArgument = "--apply-import";

    public static int? TryGetApplyImportPid(string[] args)
    {
        var raw = Find(args, ApplyImportArgument);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) && pid > 0
            ? pid
            : null;
    }

    public static string? ToCommand(string[] args)
    {
        var nav = Find(args, "--nav");

        if (!string.IsNullOrWhiteSpace(nav))
        {
            return NavCommandPrefix + nav;
        }

        var drawer = Find(args, "--drawer");

        if (!string.IsNullOrWhiteSpace(drawer))
        {
            return DrawerCommandPrefix + drawer;
        }

        return null;
    }

    private static string? Find(string[] args, string prefix)
    {
        var eq = prefix + "=";

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith(eq, StringComparison.OrdinalIgnoreCase))
            {
                return arg[eq.Length..];
            }

            if (string.Equals(arg, prefix, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
