using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.IO;
using System.Net.Http;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed partial class SourcesAuthenticationViewModelTests
{
    [Test]
    public async Task CheckAllAsync_ОднаПлощадкаЖдёт_ДругойИсточникПроверенИСНимМожноРаботать()
    {
        var plugin = Substitute.For<ISourceType, IAuthenticatable, ISourceAuthenticationProbe>();
        plugin.Name.Returns("AnotherPlatform");
        var slow = new TaskCompletionSource<SourceAuthenticationCheck>();
        ((ISourceAuthenticationProbe)plugin).CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Dictionary<string, string>>()["_system_name"] == "A"
                ? slow.Task
                : Task.FromResult(new SourceAuthenticationCheck(SourceAuthenticationStatus.Valid)));
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("slow", plugin.Name, new() { ["_system_name"] = "A" });
        context.Orcestrator.AddSource("fast", plugin.Name, new() { ["_system_name"] = "B" });
        var vm = CreateViewModel(context);
        vm.SetSources(context.Orcestrator.GetSources());
        var checking = vm.CheckAllCommand.ExecuteAsync(null);

        try
        {
            var row = vm.VisibleRows.Single(item => item.Source.Id == "fast");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(row.Check.Status, Is.EqualTo(SourceAuthenticationStatus.Valid));
                Assert.That(vm.SignInCommand.CanExecute(row), Is.True);
            }
        }
        finally
        {
            slow.SetResult(new(SourceAuthenticationStatus.CheckFailed));
            await checking;
        }
    }

    [Test]
    public async Task SetActive_ПервоеОткрытиеИВозврат_ПроверяетТолькоВключённыеБезСвежегоРезультата()
    {
        var plugin = Substitute.For<ISourceType, IAuthenticatable, ISourceAuthenticationProbe>();
        plugin.Name.Returns("AnotherPlatform");
        var probe = (ISourceAuthenticationProbe)plugin;
        probe.InspectAuthentication(Arg.Any<Dictionary<string, string>>())
            .Returns(new SourceAuthenticationLocalState(new(SourceAuthenticationStatus.Saved), []));
        probe.CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new SourceAuthenticationCheck(SourceAuthenticationStatus.Valid));
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("enabled", plugin.Name, new() { ["_system_name"] = "Рабочий" });
        context.Orcestrator.AddSource("disabled", plugin.Name, new() { ["_system_name"] = "Выключен" });
        var sources = context.Orcestrator.GetSources();
        var disabled = sources.Single(source => source.Id == "disabled");
        disabled.IsDisable = true;
        context.Orcestrator.UpdateSource(disabled);

        var vm = CreateViewModel(context);
        vm.SetSources(sources);
        vm.SetActive(true);
        vm.SetActive(false);
        vm.SetActive(true);
        var restarted = CreateViewModel(context);
        restarted.SetSources(context.Orcestrator.GetSources());
        restarted.SetActive(true);

        var recovered = restarted.VisibleRows.Single(row => !row.IsDisabled);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(recovered.State.Local.Status, Is.EqualTo(SourceAuthenticationStatus.Saved));
            Assert.That(recovered.Check.Status, Is.EqualTo(SourceAuthenticationStatus.Valid));
            Assert.That(recovered.LastSuccessfulCheck, Is.Not.Null);
            Assert.That(restarted.VisibleRows.Single(row => row.IsDisabled).CheckedAt, Is.Null);
        }
        _ = probe.Received(1).CheckAuthenticationAsync(
            Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>());
        _ = ((IAuthenticatable)plugin).DidNotReceive().AuthenticateAsync(
            Arg.Any<Dictionary<string, string>>(), Arg.Any<IAuthUI>(), Arg.Any<CancellationToken>());

        restarted.AttentionOnly = true;
        Assert.That(restarted.VisibleRows, Is.Empty);
        await restarted.CheckAllCommand.ExecuteAsync(null);
        _ = probe.Received(2).CheckAuthenticationAsync(
            Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SetSources_НастройкиИзменилисьВоВремяПроверки_ОтбрасываетСтарыйОтвет()
    {
        var plugin = Substitute.For<ISourceType, IAuthenticatable, ISourceAuthenticationProbe>();
        plugin.Name.Returns("AnotherPlatform");
        var stale = new TaskCompletionSource<SourceAuthenticationCheck>();
        ((ISourceAuthenticationProbe)plugin).CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Dictionary<string, string>>()["account"] == "old"
                ? stale.Task
                : Task.FromResult(new SourceAuthenticationCheck(SourceAuthenticationStatus.Valid, "Новый аккаунт")));
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("source", plugin.Name, new() { ["account"] = "old" });
        var vm = CreateViewModel(context);
        vm.SetSources(context.Orcestrator.GetSources());
        var oldRow = vm.VisibleRows.Single();
        var oldCheck = vm.CheckAllCommand.ExecuteAsync(null);

        var updated = context.Orcestrator.GetSources().Single();
        updated.Settings["account"] = "new";
        context.Orcestrator.UpdateSource(updated);
        vm.SetSources(context.Orcestrator.GetSources());
        vm.SetActive(true);
        stale.SetResult(new(SourceAuthenticationStatus.Rejected));
        await oldCheck;
        await vm.CheckAllAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.VisibleRows.Single().Check.Account, Is.EqualTo("Новый аккаунт"));
            Assert.That(oldRow.CheckedAt, Is.Null);
        }
    }

    [Test]
    public async Task CancelStep_ДанныеУжеЗаписаны_ПовторяетПроверкуИНеТеряетОчередь()
    {
        var plugin = Substitute.For<ISourceType, IAuthenticatable, ISourceAuthenticationProbe>();
        plugin.Name.Returns("AnotherPlatform");
        var probe = (ISourceAuthenticationProbe)plugin;
        var verifyStarted = new TaskCompletionSource();
        _ = ((IAuthenticatable)plugin).AuthenticateAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<IAuthUI>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<Dictionary<string, string>>()["session"] = "saved";
                return Task.CompletedTask;
            });
        probe.CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (!call.Arg<Dictionary<string, string>>().ContainsKey("session"))
                {
                    return Task.FromResult(new SourceAuthenticationCheck(SourceAuthenticationStatus.Missing));
                }

                verifyStarted.TrySetResult();
                return new TaskCompletionSource<SourceAuthenticationCheck>().Task.WaitAsync(call.Arg<CancellationToken>());
            });
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("first", plugin.Name, new() { ["_system_name"] = "A" });
        context.Orcestrator.AddSource("second", plugin.Name, new() { ["_system_name"] = "B" });
        var vm = CreateViewModel(context);
        vm.SetSources(context.Orcestrator.GetSources());
        await vm.CheckAllCommand.ExecuteAsync(null);

        var queueStart = vm.StartQueueCommand.ExecuteAsync(null);
        await verifyStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.CancelStepCommand.Execute(null);
        await queueStart;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Orcestrator.GetSources().Single(source => source.Id == "first").Settings["session"], Is.EqualTo("saved"));
            Assert.That(vm.QueueCurrent?.StatusText, Is.EqualTo("Ожидает проверки"));
            Assert.That(vm.QueueActionText, Is.EqualTo("Проверить сессию"));
        }

        probe.CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new SourceAuthenticationCheck(SourceAuthenticationStatus.Valid));
        await vm.ContinueQueueCommand.ExecuteAsync(null);
        Assert.That(vm.QueueCurrent?.Source.Id, Is.EqualTo("second"));
        vm.StopQueueCommand.Execute(null);
        Assert.That(vm.QueueSummary, Does.Contain("A · вход подтверждён").And.Contain("B · пропущен"));
    }

    [Test]
    public async Task SignInWithMethodAsync_ДругойСпособОтклонён_СохранённыйСпособЖдётПодтверждения()
    {
        var plugin = (ISourceType)Substitute.For(
            [typeof(ISourceType), typeof(IAuthenticatable), typeof(ISourceAuthenticationProbe), typeof(ISourceAuthenticationMethods)], []);
        plugin.Name.Returns("AnotherPlatform");
        SourceAuthenticationMethod[] methods =
        [
            new("reader", "Чтение", "Скачивание", SourceAuthenticationStatus.Rejected),
            new("writer", "Запись", "Публикация", SourceAuthenticationStatus.CheckFailed),
        ];
        var probe = (ISourceAuthenticationProbe)plugin;
        probe.InspectAuthentication(Arg.Any<Dictionary<string, string>>())
            .Returns(new SourceAuthenticationLocalState(new(SourceAuthenticationStatus.Saved, Methods: methods), []));
        probe.CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new SourceAuthenticationCheck(SourceAuthenticationStatus.Rejected, Methods: methods));
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("source", plugin.Name, []);
        var vm = CreateViewModel(context);
        vm.SetSources(context.Orcestrator.GetSources());
        var row = vm.VisibleRows.Single();

        await vm.SignInWithMethodCommand.ExecuteAsync(row.Methods.Single(method => method.Method.Id == "writer"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.AwaitingVerification, Is.True);
            Assert.That(row.StatusFor("reader"), Is.EqualTo(SourceAuthenticationStatus.Rejected));
            Assert.That(row.StatusFor("writer"), Is.EqualTo(SourceAuthenticationStatus.CheckFailed));
            Assert.That(row.CanRetryCheck, Is.True);
            Assert.That(vm.SignInCount, Is.EqualTo(1));
            Assert.That(row.Methods.Single(method => method.Method.Id == "writer").CanSignIn, Is.False);
            Assert.That(row.Methods.Single(method => method.Method.Id == "reader").CanSignIn, Is.True);
        }

        probe.CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new SourceAuthenticationCheck(SourceAuthenticationStatus.Rejected,
                Methods: [methods[0], methods[1] with { Status = SourceAuthenticationStatus.Valid }]));
        await vm.CheckCommand.ExecuteAsync(row);
        Assert.That(row.AwaitingVerification, Is.False);
        Assert.That(row.DefaultMethodId, Is.EqualTo("reader"));
        _ = ((ISourceAuthenticationMethods)plugin).Received(1).AuthenticateAsync("writer",
            Arg.Any<Dictionary<string, string>>(), Arg.Any<IAuthUI>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OpenAsync_СтарыйОтказОперации_ВыбираетИсточникиНеПерезаписываетСвежуюПроверку()
    {
        var plugin = Substitute.For<ISourceType, IAuthenticatable, ISourceAuthenticationProbe>();
        plugin.Name.Returns("AnotherPlatform");
        ((ISourceAuthenticationProbe)plugin).CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new SourceAuthenticationCheck(SourceAuthenticationStatus.Valid));
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("source", plugin.Name, []);
        var source = context.Orcestrator.GetSources().Single();
        var failure = new SourceOperationAuthenticationException(source, new("Площадка отклонила вход."));
        var vm = CreateViewModel(context);
        vm.SetSources([source]);
        var row = vm.VisibleRows.Single();
        var navigation = Substitute.For<INavigationService>();
        navigation.OpenSourceAuthenticationAsync(failure).Returns(_ => vm.FocusSourceAsync(failure));
        var recovery = new SourceAuthenticationRecoveryViewModel(navigation);
        recovery.Report(new HttpRequestException("Нет сети"));
        recovery.Report(failure);
        recovery.Report(failure);
        await vm.CheckAllCommand.ExecuteAsync(null);
        vm.AttentionOnly = true;

        await recovery.OpenCommand.ExecuteAsync(recovery.Failures.Single());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.SelectedRow, Is.SameAs(row));
            Assert.That(vm.AttentionOnly, Is.False);
            Assert.That(row.Check.Status, Is.EqualTo(SourceAuthenticationStatus.Valid));
        }
    }

    [Test]
    public async Task PublishMediaAsync_ОтказСтаройСессии_ПослеВходаРучнойПовторЧитаетНовыеНастройки()
    {
        var plugin = Substitute.For<ISourceType, IAuthenticatable>();
        plugin.Name.Returns("AnotherPlatform");
        var rejected = new SourceSessionRejectedException("Площадка отклонила токен.", methodId: "writer");
        _ = plugin.UploadAsync(Arg.Any<MediaDto>(), Arg.Any<Dictionary<string, string>>(), Arg.Any<IProgress<UploadProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Dictionary<string, string>>()["session"] == "new"
                ? Task.FromResult(new UploadResult { Id = "published", Status = MediaStatusHelper.Ok() })
                : Task.FromException<UploadResult>(rejected));
        _ = ((IAuthenticatable)plugin).AuthenticateAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<IAuthUI>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<Dictionary<string, string>>()["session"] = "new";
                return Task.CompletedTask;
            });
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("destination", plugin.Name, new() { ["session"] = "old" });
        var original = context.Orcestrator.GetSources().Single();
        var video = Path.Combine(context.Root, "video.mp4");
        File.WriteAllText(video, "stub");

        SourceOperationAuthenticationException? error = null;
        try
        {
            await context.Orcestrator.PublishMediaAsync(original, "Ролик", null, video, null);
        }
        catch (SourceOperationAuthenticationException ex)
        {
            error = ex;
        }

        Assert.That(error, Is.Not.Null);
        Assert.That(error!.SourceId, Is.EqualTo("destination"));
        Assert.That(error.MethodId, Is.EqualTo("writer"));
        var vm = CreateViewModel(context);
        vm.SetSources(context.Orcestrator.GetSources());
        await vm.FocusSourceAsync(error);
        Assert.That(vm.SelectedRow?.NeedsSignIn, Is.True);
        await vm.SignInCommand.ExecuteAsync(vm.SelectedRow);

        _ = plugin.Received(1).UploadAsync(Arg.Any<MediaDto>(), Arg.Any<Dictionary<string, string>>(),
            Arg.Any<IProgress<UploadProgress>?>(), Arg.Any<CancellationToken>());
        var published = await context.Orcestrator.PublishMediaAsync(original, "Ролик", null, video, null);
        Assert.That(published.Sources.Single().ExternalId, Is.EqualTo("published"));
    }

    [Test]
    public async Task ContinueQueueAsync_НовыйПлагин_СохраняетВходБезПотериДругихНастроек()
    {
        var plugin = Substitute.For<ISourceType, IAuthenticatable, ISourceAuthenticationProbe>();
        plugin.Name.Returns("AnotherPlatform");
        var probe = (ISourceAuthenticationProbe)plugin;
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("source", plugin.Name, new() { ["_system_name"] = "Канал" });
        var vm = CreateViewModel(context);
        vm.SetSources(context.Orcestrator.GetSources());

        probe.CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Dictionary<string, string>>().ContainsKey("test_token")
                ? new SourceAuthenticationCheck(SourceAuthenticationStatus.Valid, "Тестовый аккаунт")
                : new SourceAuthenticationCheck(SourceAuthenticationStatus.Missing));
        _ = ((IAuthenticatable)plugin).AuthenticateAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<IAuthUI>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var collection = context.Database.GetCollection<Source>("sources");
                var edited = collection.FindById("source");
                edited.Settings["other_setting"] = "сохранить";
                collection.Update(edited);
                var settings = call.Arg<Dictionary<string, string>>();
                settings["test_token"] = "test-value";
                settings["_system_name"] = "Имя из плагина";
                return Task.CompletedTask;
            });

        await vm.CheckAllCommand.ExecuteAsync(null);
        await vm.StartQueueCommand.ExecuteAsync(null);

        var saved = context.Orcestrator.GetSources().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Settings.GetValueOrDefault("test_token"), Is.EqualTo("test-value"));
            Assert.That(saved.Settings.GetValueOrDefault("other_setting"), Is.EqualTo("сохранить"));
            Assert.That(saved.Title, Is.EqualTo("Канал"));
            Assert.That(vm.VisibleRows.Single().Check.Status, Is.EqualTo(SourceAuthenticationStatus.Valid));
            Assert.That(vm.VisibleRows.Single().LastSuccessfulCheck, Is.Not.Null);
            Assert.That(vm.HasQueue, Is.False);
        }
    }

    [Test]
    public async Task ContinueQueueAsync_ПлощадкаНеОтветила_ОставляетПовторнуюПопыткуИПропуск()
    {
        var plugin = Substitute.For<ISourceType, IAuthenticatable, ISourceAuthenticationProbe>();
        plugin.Name.Returns("AnotherPlatform");
        var probe = (ISourceAuthenticationProbe)plugin;
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("first", plugin.Name, new() { ["_system_name"] = "Первый" });
        context.Orcestrator.AddSource("second", plugin.Name, new() { ["_system_name"] = "Следующий" });
        var vm = CreateViewModel(context);
        vm.SetSources(context.Orcestrator.GetSources());
        probe.CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new SourceAuthenticationCheck(SourceAuthenticationStatus.Missing));
        await vm.CheckAllCommand.ExecuteAsync(null);

        probe.CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new SourceAuthenticationCheck(SourceAuthenticationStatus.CheckFailed));
        await vm.StartQueueCommand.ExecuteAsync(null);
        var first = vm.QueueCurrent;
        Assert.That(vm.QueueActionText, Is.EqualTo("Проверить сессию"));
        await vm.ContinueQueueCommand.ExecuteAsync(null);
        _ = ((IAuthenticatable)plugin).Received(1).AuthenticateAsync(
            Arg.Any<Dictionary<string, string>>(), Arg.Any<IAuthUI>(), Arg.Any<CancellationToken>());
        var afterFailure = vm.QueueCurrent;
        vm.SkipCommand.Execute(null);
        var afterSkip = vm.QueueCurrent;
        vm.StopQueueCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterFailure, Is.SameAs(first));
            Assert.That(first?.Check.Status, Is.EqualTo(SourceAuthenticationStatus.CheckFailed));
            Assert.That(afterSkip?.Source.Id, Is.EqualTo("second"));
            Assert.That(vm.HasQueue, Is.False);
        }
    }

    private static SourcesAuthenticationViewModel CreateViewModel(TestDomainContext context) =>
        new(new SourceAuthenticationService(context.Orcestrator, NullLogger<SourceAuthenticationService>.Instance),
            (_, _, _) => Substitute.For<IAuthUI>(),
            NullLogger<SourcesAuthenticationViewModel>.Instance, context.Dialogs);

    [Test]
    public async Task CheckAllAsync_СтарыйПлагинБезСессии_ПредлагаетВход()
    {
        var plugin = Substitute.For<ISourceType, IAuthenticatable>();
        plugin.Name.Returns("LegacyPlatform");
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("legacy", plugin.Name, []);
        var vm = CreateViewModel(context);
        vm.SetSources(context.Orcestrator.GetSources());

        await vm.CheckAllCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.VisibleRows.Single().Check.Status, Is.EqualTo(SourceAuthenticationStatus.Missing));
            Assert.That(vm.StartQueueCommand.CanExecute(null), Is.True);
        }
    }

    [Test]
    public async Task SignInAsync_ПлагинуНужныНастройки_ПоказываетПричинуДоВхода()
    {
        var plugin = Substitute.For<ISourceType, IAuthenticatable, ISourceAuthenticationProbe>();
        plugin.Name.Returns("AnotherPlatform");
        ((ISourceAuthenticationProbe)plugin).InspectAuthentication(Arg.Any<Dictionary<string, string>>())
            .Returns(new SourceAuthenticationLocalState(new(SourceAuthenticationStatus.SettingsRequired, Detail: "Нужно заполнить поле."), []));
        using var context = new TestDomainContext([plugin]);
        context.Orcestrator.AddSource("source", plugin.Name, []);
        var vm = CreateViewModel(context);
        vm.SetSources(context.Orcestrator.GetSources());

        await vm.SignInCommand.ExecuteAsync(vm.VisibleRows.Single());

        var row = vm.VisibleRows.Single();
        Assert.That(row.Check.Status, Is.EqualTo(SourceAuthenticationStatus.SettingsRequired));
        Assert.That(row.Check.Detail, Is.EqualTo("Нужно заполнить поле."));
        _ = ((IAuthenticatable)plugin).DidNotReceive().AuthenticateAsync(
            Arg.Any<Dictionary<string, string>>(), Arg.Any<IAuthUI>(), Arg.Any<CancellationToken>());
    }
}
