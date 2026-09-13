namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

public sealed record Paths(
    string PluginPath,
    string DatabasePath,
    string TempPath,
    string StatePath);
