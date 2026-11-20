using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
file sealed class SessionFileAccessTests
{
    [Test]
    public async Task WriteAsync_ДвеАктивныеОперации_ЖдётОбеИБлокируетНовую()
    {
        var access = new SessionFileAccess();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var first = await access.ReadAsync("session", timeout.Token);
        using var second = await access.ReadAsync("session", timeout.Token);
        var writer = access.WriteAsync("session", timeout.Token);
        var next = access.ReadAsync("session", timeout.Token);
        using var independent = await access.WriteAsync("other-session", timeout.Token);

        first.Dispose();
        Assert.That(writer.IsCompleted, Is.False);
        second.Dispose();
        using (await writer)
        {
            Assert.That(next.IsCompleted, Is.False);
        }
        using var later = await next;
    }

    [Test]
    public async Task WriteAsync_ОжиданиеОтменено_ПропускаетНовогоЧитателя()
    {
        var access = new SessionFileAccess();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var canceled = new CancellationTokenSource();
        using var first = await access.ReadAsync("session", timeout.Token);
        var writer = access.WriteAsync("session", canceled.Token);
        var next = access.ReadAsync("session", timeout.Token);

        await canceled.CancelAsync();
        try
        {
            using var unexpected = await writer;
            Assert.Fail("Отменённая запись получила доступ.");
        }
        catch (OperationCanceledException) when (canceled.IsCancellationRequested)
        {
        }

        using var later = await next;
    }
}
