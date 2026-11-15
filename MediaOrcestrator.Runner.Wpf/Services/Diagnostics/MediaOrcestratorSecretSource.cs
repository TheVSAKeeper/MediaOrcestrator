using KeepShell.Diagnostics;
using KeepShell.Mcp;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.Services.Diagnostics;

public sealed class MediaOrcestratorSecretSource(
    McpPreferences mcp,
    Orcestrator orcestrator,
    ILogger<MediaOrcestratorSecretSource> logger) : IDiagnosticsSecretSource
{
    private static readonly HashSet<string> SecretTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "token",
        "secret",
        "password",
        "passwd",
        "passphrase",
        "credential",
        "credentials",
        "apikey",
        "cookie",
        "cookies",
        "bearer",
        "authorization",
        "oauth",
        "clientsecret",
        "signature",
        "privatekey",
        "pin",
    };

    private static readonly string[] ExtraKeyNames =
    [
        "cookie",
        "cookies",
        "auth_token",
        "session_token",
        "session_id",
        "bearer",
        "authorization",
        "password",
        "passwd",
        "passphrase",
        "signature",
        "oauth",
        "refresh_token",
        "access_token",
        "clientsecret",
        "client_secret",
        "private_key",
        "proxy_password",
    ];

    private static readonly string[] AllKeyFragments =
        [.. DiagnosticsSecretRules.CommonKeyNames, .. ExtraKeyNames];

    public DiagnosticsSecretRules Collect()
    {
        var values = new List<string>();
        var complete = true;

        try
        {
            if (!string.IsNullOrWhiteSpace(mcp.Token))
            {
                values.Add(mcp.Token.Trim());
            }
        }
        catch (Exception ex)
        {
            complete = false;
            logger.LogWarning(ex, "Не удалось прочитать токен MCP для вымарывания секретов");
        }

        try
        {
            foreach (var source in orcestrator.GetSources())
            {
                foreach (var (key, value) in source.Settings)
                {
                    if (key.StartsWith("_system", StringComparison.Ordinal)
                        || string.IsNullOrWhiteSpace(value)
                        || value.Length < 4
                        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                        || !IsSecretKey(key, value))
                    {
                        continue;
                    }

                    values.Add(value);
                }
            }
        }
        catch (Exception ex)
        {
            complete = false;
            logger.LogWarning(ex, "Не удалось собрать секреты источников для пакета диагностики");
        }

        return new DiagnosticsSecretRules(AllKeyFragments, values)
        {
            Complete = complete,
        };
    }

    private static bool IsSecretKey(string key, string value)
    {
        var tokens = SplitKeyTokens(key);

        foreach (var token in tokens)
        {
            if (SecretTokens.Contains(token))
            {
                return true;
            }
        }

        if (tokens.Count >= 2)
        {
            for (var i = 0; i < tokens.Count - 1; i++)
            {
                var pair = string.Concat(tokens[i], tokens[i + 1]);

                if (SecretTokens.Contains(pair))
                {
                    return true;
                }
            }
        }

        return tokens.Any(t => string.Equals(t, "proxy", StringComparison.OrdinalIgnoreCase))
            && value.Contains('@', StringComparison.Ordinal);
    }

    private static List<string> SplitKeyTokens(string key)
    {
        var tokens = new List<string>();
        var start = -1;

        for (var i = 0; i < key.Length; i++)
        {
            var ch = key[i];

            if (!char.IsLetterOrDigit(ch))
            {
                if (start >= 0)
                {
                    tokens.Add(key[start..i]);
                    start = -1;
                }

                continue;
            }

            if (start < 0)
            {
                start = i;
            }
            else if (char.IsUpper(ch) && char.IsLower(key[i - 1]))
            {
                tokens.Add(key[start..i]);
                start = i;
            }
        }

        if (start >= 0)
        {
            tokens.Add(key[start..]);
        }

        return tokens;
    }
}
