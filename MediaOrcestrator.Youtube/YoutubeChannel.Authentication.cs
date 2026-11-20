using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using MediaOrcestrator.Modules;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaOrcestrator.Youtube;

internal sealed partial class YoutubeChannel
{
    internal const string CookiesMethod = "cookies";
    internal const string OAuthMethod = "oauth";

    private readonly SessionFileAccess _authenticationFiles = new();

    public bool CanSwitchAccount => true;

    public async Task ReadAuthenticationFilesAsync(Dictionary<string, string> settings,
        Func<Task> read, CancellationToken cancellationToken)
    {
        using var access = await _authenticationFiles.WriteAsync(GetAuthStatePath(settings), cancellationToken);
        await read();
    }

    public async Task ReplaceAuthenticationFilesAsync(Dictionary<string, string> settings,
        Func<Task> replace, CancellationToken cancellationToken)
    {
        using var access = await _authenticationFiles.WriteAsync(GetAuthStatePath(settings), cancellationToken);
        await replace();
        EvictOAuthService(GetTokenDir(settings));
    }

    private void EvictOAuthService(string tokenDir)
    {
        if (_serviceCache.TryRemove(tokenDir, out var previous))
        {
            previous.MarkEvicted();
        }
    }

    internal static bool IsOAuthRejection(Exception? exception) => exception is
        TokenResponseException { Error.Error: "invalid_grant" } or GoogleApiException { HttpStatusCode: HttpStatusCode.Unauthorized };

    public IReadOnlyList<string> GetCopyableAuthenticationFiles(
        Dictionary<string, string> sourceSettings, Dictionary<string, string> targetSettings)
    {
        var cookiePath = GetAuthStatePath(sourceSettings);
        if (!IsOAuthConfigured(sourceSettings) || !IsOAuthConfigured(targetSettings)
            || sourceSettings["client_id"] != targetSettings["client_id"]
            || sourceSettings["client_secret"] != targetSettings["client_secret"])
        {
            return [cookiePath];
        }

        return InspectAuthentication(sourceSettings).StateFiles;
    }

    public SourceAuthenticationLocalState InspectAuthentication(Dictionary<string, string> settings)
    {
        var cookiePath = GetAuthStatePath(settings);
        var oauthConfigured = IsOAuthConfigured(settings);
        var tokenDir = GetTokenDir(settings);
        var tokenFiles = Directory.Exists(tokenDir)
            ? Directory.GetFiles(tokenDir, "Google.Apis.Auth.OAuth2.Responses.*")
            : [];
        SourceAuthenticationMethod[] methods =
        [
            new(CookiesMethod, "Cookies", "Просмотр и скачивание", File.Exists(cookiePath) ? SourceAuthenticationStatus.Saved : SourceAuthenticationStatus.Missing),
            new(OAuthMethod, "Google OAuth", "Публикация и управление видео",
                !oauthConfigured ? SourceAuthenticationStatus.SettingsRequired
                    : tokenFiles.Length > 0 ? SourceAuthenticationStatus.Saved : SourceAuthenticationStatus.Missing,
                Detail: oauthConfigured ? null : "Для публикации заполните OAuth Client ID и Client Secret.",
                CanSignIn: oauthConfigured),
        ];
        var localStatus = methods.Any(method => method.Status == SourceAuthenticationStatus.Saved)
            ? SourceAuthenticationStatus.Saved : SourceAuthenticationStatus.Missing;
        return new(new(localStatus, Methods: methods), [cookiePath, ..tokenFiles.Order(StringComparer.Ordinal)]);
    }

    public async Task<SourceAuthenticationCheck> CheckAuthenticationAsync(
        Dictionary<string, string> settings, CancellationToken cancellationToken)
    {
        using var access = await _authenticationFiles.ReadAsync(GetAuthStatePath(settings), cancellationToken);
        var local = InspectAuthentication(settings);
        var methods = await Task.WhenAll(local.Check.Methods!.Select(async method =>
        {
            if (method.Status != SourceAuthenticationStatus.Saved)
            {
                return method;
            }

            try
            {
                var check = method.Id == CookiesMethod
                    ? await CheckCookiesAsync(GetAuthStatePath(settings), cancellationToken)
                    : await CheckOAuthAsync(settings, cancellationToken);
                return method with { Status = check.Status, Account = check.Account, Detail = check.Detail };
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or GoogleApiException or TokenResponseException
                or JsonException or Newtonsoft.Json.JsonException or CookieException
                || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return method with { Status = SourceAuthenticationStatus.CheckFailed, Detail = "Не удалось получить подтверждение. Повторите проверку." };
            }
        }));
        var actionable = methods.Where(method => method.CanSignIn).ToArray();
        foreach (var status in new[] { SourceAuthenticationStatus.Rejected, SourceAuthenticationStatus.CheckFailed,
                     SourceAuthenticationStatus.Missing, SourceAuthenticationStatus.Saved })
        {
            if (actionable.Any(method => method.Status == status))
            {
                return new(status, Methods: methods);
            }
        }

        return new(SourceAuthenticationStatus.Valid, Detail: $"Подтверждено: {string.Join(", ", actionable.Select(method => method.Title))}.", Methods: methods);
    }

    public async Task AuthenticateAsync(string methodId, Dictionary<string, string> settings,
        IAuthUI ui, CancellationToken cancellationToken)
    {
        if (methodId == CookiesMethod)
        {
            await AuthenticateAsync(settings, ui, cancellationToken);
            return;
        }

        if (methodId != OAuthMethod || !IsOAuthConfigured(settings))
        {
            throw new InvalidOperationException("Для входа через Google нужны настройки OAuth.");
        }

        var tokenDir = GetTokenDir(settings);
        var stagedDir = tokenDir + "." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stagedDir);
        try
        {
            await ui.RunExternalSignInAsync(
                token => BuildCredentialAsync(settings["client_id"], settings["client_secret"], stagedDir, token,
                    new AccountChoosingReceiver()), cancellationToken);
            var tokenFile = Directory.GetFiles(stagedDir, "Google.Apis.Auth.OAuth2.Responses.*").Single();
            using var access = await _authenticationFiles.WriteAsync(GetAuthStatePath(settings), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(tokenDir);
            File.Move(tokenFile, Path.Combine(tokenDir, Path.GetFileName(tokenFile)), overwrite: true);
            EvictOAuthService(tokenDir);
        }
        finally
        {
            Directory.Delete(stagedDir, recursive: true);
        }
    }

    private async Task<SourceAuthenticationCheck> CheckOAuthAsync(
        Dictionary<string, string> settings, CancellationToken cancellationToken)
    {
        try
        {
            var store = new FileDataStore(GetTokenDir(settings), true);
            using var flow = new GoogleAuthorizationCodeFlow(new()
            {
                ClientSecrets = new()
                {
                    ClientId = settings["client_id"],
                    ClientSecret = settings["client_secret"],
                },
                DataStore = store,
                Scopes = OAuthScopes,
            });
            var token = await flow.LoadTokenAsync("user", cancellationToken);
            if (token is null)
            {
                return new(SourceAuthenticationStatus.Missing);
            }

            var credential = new UserCredential(flow, "user", token);
            using var service = youtubeServiceFactory.Create(credential, settings.GetValueOrDefault("app_name", "MediaOrcestrator"));
            var request = service.Channels.List("snippet");
            request.Mine = true;
            var result = await request.ExecuteAsync(cancellationToken);
            return new(SourceAuthenticationStatus.Valid,
                result.Items?.FirstOrDefault()?.Snippet?.Title,
                "YouTube Data API подтвердила вход через OAuth.");
        }
        catch (TokenResponseException ex) when (ex.Error?.Error == "invalid_grant")
        {
            return new(SourceAuthenticationStatus.Rejected, Detail: "Google отклонил сохранённый OAuth-токен.");
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            return new(SourceAuthenticationStatus.Rejected, Detail: "YouTube Data API отклонила данные входа.");
        }
    }

    private static async Task<SourceAuthenticationCheck> CheckCookiesAsync(string path, CancellationToken cancellationToken)
    {
        var uri = new Uri("https://www.youtube.com/feed/history");
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        };

        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken))
        {
            var entry = line.StartsWith("#HttpOnly_", StringComparison.Ordinal) ? line[10..] : line;
            if (entry.StartsWith('#'))
            {
                continue;
            }

            var fields = entry.Split('\t');
            if (fields.Length != 7 || fields[2] != "/" || !long.TryParse(fields[4], CultureInfo.InvariantCulture, out var expiry))
            {
                continue;
            }

            var domain = fields[0].TrimStart('.');
            if (!domain.Equals(uri.Host, StringComparison.OrdinalIgnoreCase)
                && !(fields[1] == "TRUE" && uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (expiry > 0 && expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                continue;
            }

            handler.CookieContainer.Add(uri, new Cookie(fields[5], fields[6], "/") { Secure = fields[3] == "TRUE" });
        }

        using var client = new HttpClient(handler, disposeHandler: false);
        using var response = await client.GetAsync(uri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized
            || response.Headers.Location is { IsAbsoluteUri: true, Host: "accounts.google.com" })
        {
            return new(SourceAuthenticationStatus.Rejected, Detail: "YouTube запросил повторный вход в браузере.");
        }

        if (response.Headers.Location is { IsAbsoluteUri: true, Host: "consent.youtube.com" or "consent.google.com" })
        {
            return new(SourceAuthenticationStatus.Rejected,
                Detail: "YouTube просит подтвердить настройки конфиденциальности. Откройте YouTube в окне входа и завершите этот шаг.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return new(SourceAuthenticationStatus.CheckFailed, Detail: $"YouTube ответил HTTP {(int)response.StatusCode}.");
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var login = LoginMarker().Match(html);
        return login.Groups[1].Value switch
        {
            "true" => new(SourceAuthenticationStatus.Valid, Detail: "YouTube подтвердил вход по cookies."),
            "false" => new(SourceAuthenticationStatus.Rejected, Detail: "YouTube видит браузер как гостя."),
            _ => new(SourceAuthenticationStatus.Saved, Detail: "YouTube не подтвердил вход по сохранённым cookies."),
        };
    }

    internal static async Task WriteBrowserSeedAsync(string cookiePath, string outputPath, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var lines = await File.ReadAllLinesAsync(cookiePath, cancellationToken);
        var cookies = lines.Select(line =>
        {
            var httpOnly = line.StartsWith("#HttpOnly_", StringComparison.Ordinal);
            var fields = (httpOnly ? line[10..] : line).Split('\t');
            var expires = fields.Length == 7 && long.TryParse(fields[4], CultureInfo.InvariantCulture, out var seconds)
                ? (long?)seconds : null;
            return (HttpOnly: httpOnly, Fields: fields, Expires: expires);
        })
        .Where(cookie => cookie.Fields is [var domain, _, var path, _, _, var name, _]
            && Uri.CheckHostName(domain.TrimStart('.')) == UriHostNameType.Dns && path.StartsWith('/') && name.Length > 0
            && cookie.Expires is { } seconds && (seconds <= 0 || seconds > now))
        .Select(cookie => new
        {
            domain = BrowserCookieDomain(cookie.Fields[0], cookie.Fields[1], cookie.Fields[5]),
            path = cookie.Fields[2],
            secure = cookie.Fields[3] == "TRUE",
            expires = cookie.Expires > 0 ? cookie.Expires.GetValueOrDefault() : -1,
            name = cookie.Fields[5],
            value = cookie.Fields[6],
            httpOnly = cookie.HttpOnly,
            sameSite = "Lax",
        });
        await using var output = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(output, new { cookies, origins = Array.Empty<object>() }, cancellationToken: cancellationToken);
    }

    private static string BrowserCookieDomain(string domain, string includeSubdomains, string name)
    {
        var host = domain.TrimStart('.');
        return includeSubdomains == "TRUE" && !name.StartsWith("__Host-", StringComparison.OrdinalIgnoreCase)
            ? "." + host
            : host;
    }

    [GeneratedRegex("\"LOGGED_IN\"\\s*:\\s*(true|false)", RegexOptions.NonBacktracking)]
    private static partial Regex LoginMarker();

    private sealed class AccountChoosingReceiver : ICodeReceiver
    {
        private readonly LocalServerCodeReceiver _receiver = new();

        public string RedirectUri => _receiver.RedirectUri;

        public Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(AuthorizationCodeRequestUrl url, CancellationToken cancellationToken)
        {
            if (url is GoogleAuthorizationCodeRequestUrl google)
            {
                google.Prompt = "select_account consent";
            }

            return _receiver.ReceiveCodeAsync(url, cancellationToken);
        }
    }
}
