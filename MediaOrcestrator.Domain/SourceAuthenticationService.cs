using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MediaOrcestrator.Domain;

public sealed partial class SourceAuthenticationService(Orcestrator orcestrator, ILogger<SourceAuthenticationService> logger)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sourceLocks = new(StringComparer.Ordinal);

    private SemaphoreSlim GetSourceLock(string sourceId) => _sourceLocks.GetOrAdd(sourceId, _ => new(1, 1));

    public SourceAuthenticationState ReadState(Source source)
    {
        var local = Inspect(source);
        var revision = GetRevision(source, local.StateFiles);
        var stamp = orcestrator.ReadAuthenticationStamp(source.Id);
        var sameSession = stamp?.Revision == revision;
        if (stamp is null || !sameSession && stamp.SettingsRevision != GetRevision(source, []))
        {
            return new(local.Check, revision);
        }

        var knownLocal = WithKnownAccounts(local.Check, stamp.LastKnownAccount, stamp.MethodAccounts);
        if (!sameSession)
        {
            return new(knownLocal, revision);
        }

        var methods = knownLocal.Methods?.Select(method =>
            stamp.MethodStatuses.TryGetValue(method.Id, out var status) ? method with { Status = status } : method).ToArray();
        return new(knownLocal, revision, new(stamp.Status, stamp.LastKnownAccount, Methods: methods), stamp.CheckedAt.ToUniversalTime(),
            stamp.LastConfirmedAt?.ToUniversalTime());
    }

    public void Invalidate(string sourceId) => orcestrator.ResetAuthenticationConfirmation(sourceId);

    public SourceAuthenticationLocalState Inspect(Source source)
    {
        try
        {
            var settings = CopySettings(source);
            if (source.Type is ISourceAuthenticationProbe probe && probe.InspectAuthentication(settings) is { } state)
            {
                return state;
            }

            if (source.Type is not ISourceAuthenticationProbe && source.Type is IAuthenticatable auth)
            {
                return new(new(auth.IsAuthenticated(settings) ? SourceAuthenticationStatus.Saved : SourceAuthenticationStatus.Missing), []);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Не удалось прочитать данные входа источника {SourceId}: {ErrorType}", source.Id, ex.GetType().Name);
        }

        return new(new(SourceAuthenticationStatus.None), []);
    }

    public SourceAuthenticationState RecordCheck(Source source, SourceAuthenticationCheck check, DateTime? observedAt = null)
    {
        var current = ReadState(source);
        var checkedAt = observedAt ?? DateTime.UtcNow;
        var lastConfirmedAt = check.Status == SourceAuthenticationStatus.Valid
            ? checkedAt
            : current.LastConfirmedAt;
        var previous = current.LastCheck ?? current.Local;
        var methodAccounts = (previous.Methods ?? []).Where(method => !string.IsNullOrWhiteSpace(method.Account))
            .ToDictionary(method => method.Id, method => method.Account!);
        foreach (var method in check.Methods ?? [])
        {
            if (method.Status == SourceAuthenticationStatus.Valid)
            {
                if (!string.IsNullOrWhiteSpace(method.Account))
                {
                    methodAccounts[method.Id] = method.Account;
                }
                else
                {
                    methodAccounts.Remove(method.Id);
                }
            }
        }

        var account = check.Status == SourceAuthenticationStatus.Valid
            ? (string.IsNullOrWhiteSpace(check.Account) ? null : check.Account)
            : previous.Account;
        var result = WithKnownAccounts(check with { Methods = check.Methods ?? current.Local.Methods }, account, methodAccounts);
        orcestrator.SaveAuthenticationStamp(new()
        {
            Id = source.Id,
            Revision = current.Revision,
            SettingsRevision = GetRevision(source, []),
            Status = check.Status,
            CheckedAt = checkedAt,
            LastConfirmedAt = lastConfirmedAt,
            MethodStatuses = check.Methods?.ToDictionary(method => method.Id, method => method.Status) ?? [],
            LastKnownAccount = account,
            MethodAccounts = methodAccounts,
        });
        return current with { LastCheck = result, CheckedAt = checkedAt, LastConfirmedAt = lastConfirmedAt };
    }

    private static SourceAuthenticationCheck WithKnownAccounts(SourceAuthenticationCheck check, string? account,
        IReadOnlyDictionary<string, string> methodAccounts) => check with
    {
        Account = account,
        Methods = check.Methods?.Select(method => method with { Account = methodAccounts.GetValueOrDefault(method.Id) }).ToArray(),
    };

    public async Task<SourceAuthenticationCheck> CheckAsync(Source source, CancellationToken cancellationToken)
    {
        var gate = GetSourceLock(source.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await CheckCoreAsync(source, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<SourceAuthenticationState> CheckAndRecordAsync(Source source, CancellationToken cancellationToken)
    {
        var gate = GetSourceLock(source.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var result = await CheckCoreAsync(source, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return RecordCheck(source, result);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<SourceAuthenticationState?> RecordOperationRejectionAsync(Source source,
        SourceOperationAuthenticationException failure)
    {
        var gate = GetSourceLock(source.Id);
        await gate.WaitAsync();
        try
        {
            var state = ReadState(source);
            if (state.Revision != failure.Revision || state.CheckedAt > failure.ObservedAt)
            {
                return null;
            }

            var methods = (state.LastCheck ?? state.Local).Methods?.Select(method => method.Id == failure.MethodId
                ? method with { Status = SourceAuthenticationStatus.Rejected } : method).ToArray();
            return RecordCheck(source, new(SourceAuthenticationStatus.Rejected,
                Detail: "Площадка отклонила вход во время операции.", Methods: methods), failure.ObservedAt);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SourceAuthenticationCheck> CheckCoreAsync(Source source, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            var settings = CopySettings(source);
            if (source.Type is ISourceAuthenticationProbe probe)
            {
                var result = await probe.CheckAuthenticationAsync(settings, timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                var failedMethods = result.Methods?.Where(method => method.Status == SourceAuthenticationStatus.CheckFailed)
                    .Select(method => method.Id).ToArray() ?? [];
                if (result.Status == SourceAuthenticationStatus.CheckFailed || failedMethods.Length > 0)
                {
                    logger.LogWarning("Проверка сессии источника {SourceId} ({Platform}) не завершена. Способы с ошибкой: {FailedMethods}",
                        source.Id, source.TypeId, failedMethods);
                }
                return result;
            }

            if (source.Type is IAuthenticatable auth)
            {
                return auth.IsAuthenticated(settings)
                    ? new(SourceAuthenticationStatus.Saved)
                    : new(SourceAuthenticationStatus.Missing);
            }

            return new(SourceAuthenticationStatus.CheckFailed, Detail: "Плагин не сообщает состояние сессии.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            logger.LogWarning("Истекло время проверки сессии источника {SourceId} ({Platform})", source.Id, source.TypeId);
            return new(SourceAuthenticationStatus.CheckFailed, Detail: "Истекло время ожидания ответа площадки.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Проверка авторизации источника {SourceId} завершилась ошибкой {ErrorType}",
                source.Id, ex.GetType().Name);
            return new(SourceAuthenticationStatus.CheckFailed, Detail: "Ошибка проверки. Попробуйте ещё раз.");
        }
    }

    public async Task SaveSignInAsync(Source source, IAuthUI ui, string? methodId, CancellationToken cancellationToken)
    {
        var gate = GetSourceLock(source.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await SaveSignInCoreAsync(source, ui, methodId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SaveSignInCoreAsync(Source source, IAuthUI ui, string? methodId, CancellationToken cancellationToken)
    {
        if (source.Type is not IAuthenticatable auth)
        {
            throw new InvalidOperationException("Источник не поддерживает вход.");
        }

        var original = CopySettings(source);
        var updated = new Dictionary<string, string>(original, original.Comparer);
        if (methodId is not null && source.Type is ISourceAuthenticationMethods methods)
        {
            await methods.AuthenticateAsync(methodId, updated, ui, cancellationToken);
        }
        else
        {
            await auth.AuthenticateAsync(updated, ui, cancellationToken);
        }

        orcestrator.SaveAuthenticationSettings(source, original, updated);
    }

    private static Dictionary<string, string> CopySettings(Source source) =>
        new(source.Settings, source.Settings.Comparer);

    internal static string ComputeRevision(Source source)
    {
        try
        {
            var settings = CopySettings(source);
            var stateFiles = source.Type is ISourceAuthenticationProbe probe
                && probe.InspectAuthentication(settings) is { } state
                ? state.StateFiles
                : [];
            return GetRevision(source, stateFiles);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetRevision(Source source, IReadOnlyList<string> stateFiles)
    {
        var settings = source.Settings.Where(item => !item.Key.StartsWith("_system_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Key, StringComparer.Ordinal);
        var files = stateFiles.Select(path =>
        {
            var file = new FileInfo(path);
            return new { path, Exists = file.Exists, Modified = file.LastWriteTimeUtc, Length = file.Exists ? file.Length : 0 };
        });
        var data = JsonSerializer.Serialize(new
        {
            source.TypeId,
            StatePath = source.Settings.GetValueOrDefault("_system_state_path"),
            Settings = settings,
            Files = files,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
    }
}

public sealed record SourceAuthenticationState(
    SourceAuthenticationCheck Local,
    string Revision,
    SourceAuthenticationCheck? LastCheck = null,
    DateTime? CheckedAt = null,
    DateTime? LastConfirmedAt = null);

internal sealed class SourceAuthenticationStamp
{
    public string Id { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public string? SettingsRevision { get; set; }
    public SourceAuthenticationStatus Status { get; set; }
    public DateTime CheckedAt { get; set; }
    public DateTime? LastConfirmedAt { get; set; }
    public Dictionary<string, SourceAuthenticationStatus> MethodStatuses { get; set; } = [];
    public string? LastKnownAccount { get; set; }
    public Dictionary<string, string> MethodAccounts { get; set; } = [];
}
