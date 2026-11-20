using MediaOrcestrator.Youtube;
using System.Text.Json;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
file sealed class YoutubeAuthenticationTests
{
    [TestCase("WARNING: [youtube] The provided YouTube account cookies are no longer valid.", true)]
    [TestCase("ERROR: [youtube] Sign in to confirm you’re not a bot.", false)]
    [TestCase("ERROR: [youtube] HTTP Error 403: Forbidden", false)]
    public void IsCookieRejection_ВыводYtDlp_ОтличаетОтказСессии(string output, bool expected) =>
        Assert.That(YtDlp.IsCookieRejection(output), Is.EqualTo(expected));

    [TestCase(true)]
    [TestCase(false)]
    public async Task CheckAuthenticationAsync_ОшибкаOAuth_СохраняетСостояниеCookies(bool corruptToken)
    {
        var directory = Directory.CreateTempSubdirectory("youtube-auth-");
        try
        {
            var oauth = Directory.CreateDirectory(Path.Combine(directory.FullName, "oauth"));
            File.WriteAllText(Path.Combine(oauth.FullName, "Google.Apis.Auth.OAuth2.Responses.TokenResponse-user"),
                corruptToken ? "invalid JSON" : "{}");
            var services = Substitute.For<IYoutubeServiceFactory>();
            services.Create(Arg.Any<Google.Apis.Auth.OAuth2.UserCredential>(), Arg.Any<string>())
                .Throws(new TaskCanceledException("Timeout"));
            var channel = new YoutubeChannel(NullLogger<YoutubeChannel>.Instance, services, null!, null!, null!,
                null!, null!, null!, null!, null!, null!);

            var result = await channel.CheckAuthenticationAsync(new()
            {
                ["_system_state_path"] = directory.FullName,
                ["client_id"] = "client",
                ["client_secret"] = "secret",
            }, CancellationToken.None);

            Assert.That(result.Methods?.Select(method => method.Status),
                Is.EqualTo(new[] { SourceAuthenticationStatus.Missing, SourceAuthenticationStatus.CheckFailed }));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task WriteBrowserSeedAsync_СтарыеCookies_ВосстанавливаетОбластьИОтбрасываетИстёкшие()
    {
        var directory = Directory.CreateTempSubdirectory("youtube-auth-");
        try
        {
            var file = Path.Combine(directory.FullName, "auth_state");
            var seed = Path.Combine(directory.FullName, "seed.json");
            await File.WriteAllLinesAsync(file,
            [
                "# Netscape HTTP Cookie File",
                "#HttpOnly_.accounts.google.com\tTRUE\t/\tTRUE\t0\t__Host-sample\tvalue",
                "youtube.com\tTRUE\t/\tTRUE\t-1\tdomain-cookie\tvalue",
                ".www.youtube.com\tFALSE\t/\tTRUE\t0\thost-cookie\tvalue",
                ".youtube.com\tTRUE\t/\tTRUE\t1\texpired\tvalue",
            ]);

            await YoutubeChannel.WriteBrowserSeedAsync(file, seed, CancellationToken.None);

            using var state = JsonDocument.Parse(await File.ReadAllTextAsync(seed));
            var cookies = state.RootElement.GetProperty("cookies").EnumerateArray().ToArray();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(cookies.Select(cookie => cookie.GetProperty("domain").GetString()),
                    Is.EqualTo(new[] { "accounts.google.com", ".youtube.com", "www.youtube.com" }));
                Assert.That(cookies.Select(cookie => cookie.GetProperty("expires").GetInt64()), Is.All.EqualTo(-1));
                Assert.That(cookies[0].GetProperty("httpOnly").GetBoolean(), Is.True);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
