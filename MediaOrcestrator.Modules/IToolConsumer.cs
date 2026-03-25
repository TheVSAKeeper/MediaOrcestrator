namespace MediaOrcestrator.Modules;

public interface IToolConsumer
{
    IReadOnlyList<ToolDescriptor> RequiredTools { get; }
    void SetToolPath(string toolName, string? resolvedPath);
    string? GetLegacyToolPath(string toolName);
}
