namespace MediaOrcestrator.Modules;

public interface ISourceAuthenticationProbe
{
    bool CanSwitchAccount => false;

    SourceAuthenticationLocalState? InspectAuthentication(Dictionary<string, string> settings) => null;

    IReadOnlyList<string> GetCopyableAuthenticationFiles(
        Dictionary<string, string> sourceSettings, Dictionary<string, string> targetSettings) =>
        InspectAuthentication(targetSettings)?.Check.Status == SourceAuthenticationStatus.SettingsRequired
            ? []
            : InspectAuthentication(sourceSettings)?.StateFiles ?? [];

    Task ReadAuthenticationFilesAsync(Dictionary<string, string> settings,
        Func<Task> read, CancellationToken cancellationToken) => read();

    Task ReplaceAuthenticationFilesAsync(Dictionary<string, string> settings,
        Func<Task> replace, CancellationToken cancellationToken) => replace();

    Task<SourceAuthenticationCheck> CheckAuthenticationAsync(
        Dictionary<string, string> settings,
        CancellationToken cancellationToken);
}

public interface ISourceAuthenticationMethods : IAuthenticatable
{
    Task AuthenticateAsync(string methodId, Dictionary<string, string> settings, IAuthUI ui, CancellationToken cancellationToken);
}

public sealed record BrowserAuthenticationRequest(
    string Url,
    string OutputStatePath,
    string? ExistingStatePath,
    Func<string, CancellationToken, Task<SourceAuthenticationCheck>> VerifyAsync);

public sealed record SourceAuthenticationLocalState(
    SourceAuthenticationCheck Check,
    IReadOnlyList<string> StateFiles);

public enum SourceAuthenticationStatus
{
    None = 0,
    Missing = 1,
    Saved = 2,
    Valid = 3,
    Rejected = 4,
    CheckFailed = 5,
    SettingsRequired = 6,
}

public sealed record SourceAuthenticationCheck(
    SourceAuthenticationStatus Status,
    string? Account = null,
    string? Detail = null,
    IReadOnlyList<SourceAuthenticationMethod>? Methods = null);

public sealed record SourceAuthenticationMethod(
    string Id,
    string Title,
    string Usage,
    SourceAuthenticationStatus Status,
    string? Account = null,
    string? Detail = null,
    bool CanSignIn = true)
{
    public bool? CanSwitchAccount { get; init; }
}

public class SourceSessionRejectedException(
    string message,
    Exception? innerException = null,
    string? methodId = null) : NonRetriableException(message, innerException)
{
    public string? MethodId { get; } = methodId;
}
