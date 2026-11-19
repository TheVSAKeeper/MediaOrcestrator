using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Domain;

public sealed class SourceOperationAuthenticationException(Source source, SourceSessionRejectedException cause)
    : NonRetriableException($"Площадка отклонила вход для «{source.TitleFull}».", cause)
{
    public string SourceId { get; } = source.Id;
    public string SourceTitle { get; } = source.TitleFull;
    public string? MethodId { get; } = cause.MethodId;
    public DateTime ObservedAt { get; } = DateTime.UtcNow;
    public string Revision { get; } = SourceAuthenticationService.ComputeRevision(source);
}
