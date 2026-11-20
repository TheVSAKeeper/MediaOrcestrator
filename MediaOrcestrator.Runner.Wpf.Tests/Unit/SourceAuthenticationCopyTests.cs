using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class SourceAuthenticationCopyTests
{
    [Test]
    public async Task CopyAuthenticationAsync_ИсходнуюСтрокуУдалилиВДиалоге_ПоказываетПредупреждение()
    {
        var plugin = Substitute.For<ISourceType, ISourceAuthenticationProbe>();
        plugin.Name.Returns("TestPlatform");
        var probe = (ISourceAuthenticationProbe)plugin;
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("origin", plugin.Name, []);
        context.Orcestrator.AddSource("target", plugin.Name, []);
        var sources = context.Orcestrator.GetSources().ToArray();
        var origin = sources.Single(source => source.Id == "origin");
        var target = sources.Single(source => source.Id == "target");
        var file = Path.Combine(origin.Settings["_system_state_path"], "session");
        File.WriteAllText(file, "session");
        probe.InspectAuthentication(Arg.Any<Dictionary<string, string>>())
            .Returns(new SourceAuthenticationLocalState(new(SourceAuthenticationStatus.Saved), []));
        probe.GetCopyableAuthenticationFiles(Arg.Any<Dictionary<string, string>>(), Arg.Any<Dictionary<string, string>>())
            .Returns([file]);
        probe.CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new SourceAuthenticationCheck(SourceAuthenticationStatus.Rejected));
        var vm = new SourcesAuthenticationViewModel(new(context.Orcestrator, NullLogger<SourceAuthenticationService>.Instance),
            (_, _, _) => Substitute.For<IAuthUI>(), NullLogger<SourcesAuthenticationViewModel>.Instance, context.Dialogs);
        vm.SetSources(sources);
        await vm.CheckAllCommand.ExecuteAsync(null);
        context.Dialogs.ShowAsync(Arg.Any<CopyAuthenticationViewModel>()).Returns(call =>
        {
            var dialog = call.Arg<CopyAuthenticationViewModel>();
            Assert.That(dialog.OriginWarning, Does.Contain("отклонила сессию"));
            dialog.Target = dialog.Targets.Single();
            vm.SetSources([target]);
            return Task.FromResult(true);
        });

        await vm.CopyAuthenticationCommand.ExecuteAsync(vm.VisibleRows.Single(row => row.Source.Id == origin.Id));

        context.Dialogs.Received(1).Warning("Копирование авторизации", Arg.Is<string>(value => value.Contains("изменился")));
        Assert.That(File.Exists(Path.Combine(target.Settings["_system_state_path"], "session")), Is.False);
    }

    [TestCase(SourceAuthenticationStatus.Rejected)]
    [TestCase(SourceAuthenticationStatus.CheckFailed)]
    [TestCase(SourceAuthenticationStatus.Valid)]
    public async Task CopyAuthenticationAsync_ПрошлыйАккаунтПолучателя_СбрасываетИмя(SourceAuthenticationStatus status)
    {
        var plugin = Substitute.For<ISourceType, ISourceAuthenticationProbe>();
        plugin.Name.Returns("TestPlatform");
        var probe = (ISourceAuthenticationProbe)plugin;
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("origin", plugin.Name, []);
        context.Orcestrator.AddSource("target", plugin.Name, []);
        var sources = context.Orcestrator.GetSources().ToArray();
        var origin = sources.Single(source => source.Id == "origin");
        var target = sources.Single(source => source.Id == "target");
        static string Session(Dictionary<string, string> settings) => Path.Combine(settings["_system_state_path"], "session");
        var method = new SourceAuthenticationMethod("browser", "Браузер", "Вход", SourceAuthenticationStatus.Saved);
        probe.InspectAuthentication(Arg.Any<Dictionary<string, string>>()).Returns(call =>
            new SourceAuthenticationLocalState(new(SourceAuthenticationStatus.Saved, Methods: [method]),
                [Session(call.Arg<Dictionary<string, string>>())]));
        probe.GetCopyableAuthenticationFiles(Arg.Any<Dictionary<string, string>>(), Arg.Any<Dictionary<string, string>>())
            .Returns([Session(origin.Settings)]);
        probe.ReadAuthenticationFilesAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task>>()());
        probe.ReplaceAuthenticationFilesAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task>>()());
        File.WriteAllText(Session(origin.Settings), "new session");
        File.WriteAllText(Session(target.Settings), "old session");
        var service = new SourceAuthenticationService(context.Orcestrator, NullLogger<SourceAuthenticationService>.Instance);
        service.RecordCheck(target, new(SourceAuthenticationStatus.Valid, "Старый аккаунт",
            Methods: [method with { Status = SourceAuthenticationStatus.Valid, Account = "Старый аккаунт" }]));

        await service.CopyAuthenticationAsync(origin, target, CancellationToken.None);
        var copied = service.ReadState(target);
        var checkedState = service.RecordCheck(target, new(status, Methods: [method with { Status = status }]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(copied.Local.Account, Is.Null);
            Assert.That(copied.Local.Methods!.Single().Account, Is.Null);
            Assert.That(checkedState.LastCheck!.Account, Is.Null);
            Assert.That(checkedState.LastCheck.Methods!.Single().Account, Is.Null);
        }
    }

    [Test]
    public async Task CopyAuthenticationAsync_ВтораяЗаписьНедоступна_ВосстанавливаетПервую()
    {
        var plugin = Substitute.For<ISourceType, ISourceAuthenticationProbe>();
        plugin.Name.Returns("TestPlatform");
        var probe = (ISourceAuthenticationProbe)plugin;
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("origin", plugin.Name, []);
        context.Orcestrator.AddSource("target", plugin.Name, []);
        var sources = context.Orcestrator.GetSources().ToArray();
        var origin = sources.Single(source => source.Id == "origin");
        var target = sources.Single(source => source.Id == "target");
        var originRoot = origin.Settings["_system_state_path"];
        var targetRoot = target.Settings["_system_state_path"];
        string[] files = [Path.Combine(originRoot, "first"), Path.Combine(originRoot, "second")];
        probe.GetCopyableAuthenticationFiles(Arg.Any<Dictionary<string, string>>(), Arg.Any<Dictionary<string, string>>())
            .Returns(files);
        probe.ReadAuthenticationFilesAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task>>()());
        probe.ReplaceAuthenticationFilesAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task>>()());
        File.WriteAllText(files[0], "origin first");
        File.WriteAllText(files[1], "origin second");
        var previous = Path.Combine(targetRoot, "first");
        File.WriteAllText(previous, "target first");
        Directory.CreateDirectory(Path.Combine(targetRoot, "second"));

        var service = new SourceAuthenticationService(context.Orcestrator, NullLogger<SourceAuthenticationService>.Instance);
        Exception? error = null;
        try
        {
            await service.CopyAuthenticationAsync(origin, target, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(File.ReadAllText(previous), Is.EqualTo("target first"));
            Assert.That(File.ReadAllText(files[0]), Is.EqualTo("origin first"));
            Assert.That(Directory.GetDirectories(targetRoot, ".auth-copy-*"), Is.Empty);
        }
    }
}
