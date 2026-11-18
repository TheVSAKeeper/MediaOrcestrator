using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using KeepShell.ViewModels;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using NSubstitute;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

public sealed partial class SourcesAuthenticationViewModelTests
{
    [Test]
    public async Task SaveCandidateAsync_ПлощадкаВидитГостя_СохраняетСтарыйФайл()
    {
        using var context = new TestDomainContext();
        var state = Path.Combine(context.Root, "state.json");
        await File.WriteAllTextAsync(state, "сохранённый вход");
        var browser = Substitute.For<IBrowserContext>();
        browser.StorageStateAsync().Returns("новая сессия");
        var ui = new WpfAuthUI(context.Dialogs, new ModalHostViewModel(), NullLogger<WpfAuthUI>.Instance);
        var rejected = await ui.SaveCandidateAsync(browser, state, (path, _) =>
        {
            Assert.That(File.ReadAllText(path), Is.EqualTo("новая сессия"));
            return Task.FromResult(new SourceAuthenticationCheck(SourceAuthenticationStatus.Rejected, Detail: "Площадка видит гостя."));
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rejected, Is.EqualTo("Площадка видит гостя."));
            Assert.That(File.ReadAllText(state), Is.EqualTo("сохранённый вход"));
            Assert.That(Directory.GetFiles(context.Root), Is.EqualTo(new[] { state }));
            Assert.That(WpfAuthUI.CreateBrowserOptions(state).StorageStatePath, Is.EqualTo(state));
            Assert.That(WpfAuthUI.CreateBrowserOptions(null).StorageStatePath, Is.Null);
        }

        var accepted = await ui.SaveCandidateAsync(browser, state,
            (_, _) => Task.FromResult(new SourceAuthenticationCheck(SourceAuthenticationStatus.Valid)), CancellationToken.None);
        Assert.That(accepted, Is.Null);
        Assert.That(File.ReadAllText(state), Is.EqualTo("новая сессия"));
    }

    [Test]
    public async Task Раскрытие_ОдинаковыеПлощадки_НезависимоОтВыделенияИПодсчёта()
    {
        var plugin = Substitute.For<ISourceType, IAuthenticatable, ISourceAuthenticationProbe>();
        plugin.Name.Returns("AnotherPlatform");
        ((ISourceAuthenticationProbe)plugin).CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(call => new SourceAuthenticationCheck(call.Arg<Dictionary<string, string>>()["_system_name"] == "Первый"
                ? SourceAuthenticationStatus.Rejected : SourceAuthenticationStatus.CheckFailed));
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("one", plugin.Name, new() { ["_system_name"] = "Первый" });
        context.Orcestrator.AddSource("two", plugin.Name, new() { ["_system_name"] = "Второй" });
        var vm = CreateViewModel(context);
        vm.SetSources(context.Orcestrator.GetSources());
        await vm.CheckAllCommand.ExecuteAsync(null);
        var first = vm.VisibleRows.Single(row => row.Source.Id == "one");
        var second = vm.VisibleRows.Single(row => row.Source.Id == "two");

        vm.ToggleDetailsCommand.Execute(first);
        vm.SelectedRow = second;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsExpanded, Is.True);
            Assert.That(second.IsExpanded, Is.False);
            Assert.That(first.ShowInlineSignIn, Is.False);
            Assert.That(vm.Summary.Sum(group => group.Count), Is.EqualTo(2));
            Assert.That(vm.AttentionCount, Is.EqualTo(2));
        }

        Assert.That(vm.CloseDetails(), Is.True);
        Assert.That(first.ShowInlineSignIn, Is.True);
        var errors = vm.Summary.Single(group => group.Status == SourceAuthenticationStatus.CheckFailed);
        vm.FilterByStatusCommand.Execute(errors);
        Assert.That(vm.VisibleRows, Is.EqualTo(new[] { second }));

        await vm.StartQueueCommand.ExecuteAsync(null);
        Assert.That(vm.VisibleRows.First(), Is.SameAs(first));
        Assert.That(first.IsQueueCurrent, Is.True);
    }

    [Test]
    public async Task CheckAllAsync_СессияОтклоненаПослеПодтверждения_КаналВиденПослеПерезапуска()
    {
        var plugin = Substitute.For<ISourceType, IAuthenticatable, ISourceAuthenticationProbe>();
        plugin.Name.Returns("AnotherPlatform");
        var probe = (ISourceAuthenticationProbe)plugin;
        probe.CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new SourceAuthenticationCheck(SourceAuthenticationStatus.Valid, Account: "Образовательный канал"));
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("education", plugin.Name, []);
        var vm = CreateViewModel(context);
        vm.SetSources(context.Orcestrator.GetSources());
        await vm.CheckAllCommand.ExecuteAsync(null);

        probe.CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new SourceAuthenticationCheck(SourceAuthenticationStatus.Rejected));
        await vm.CheckAllCommand.ExecuteAsync(null);
        var restarted = CreateViewModel(context);
        restarted.SetSources(context.Orcestrator.GetSources());

        Assert.That(restarted.VisibleRows.Single().Check.Account, Is.EqualTo("Образовательный канал"));
    }
}
