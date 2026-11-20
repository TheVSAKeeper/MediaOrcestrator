using KeepShell.Testing;
using KeepShell.Services.Platform;
using KeepShell.Views;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MediaOrcestrator.Runner.Wpf.Tests.Smoke;

public sealed partial class LiveSmokeTests
{
    [TestCase(AppThemes.LightKey)]
    [TestCase(AppThemes.DarkKey)]
    [TestCase(AppThemes.TarkovKey)]
    public void Авторизация_КнопкаКопирования_ВыбираетПолучателяИПроверяетСессию(string theme)
    {
        using var suppressed = ThemeTransitionOverlay.Suppress();
        var sources = _services.GetRequiredService<SourcesRelationsViewModel>();
        var previousMode = sources.ViewMode;
        var auth = sources.Authentication;
        var root = Directory.CreateTempSubdirectory("copy-auth-");
        var plugin = Substitute.For<ISourceType, ISourceAuthenticationProbe>();
        var probe = (ISourceAuthenticationProbe)plugin;
        probe.InspectAuthentication(Arg.Any<Dictionary<string, string>>())
            .Returns(call => new SourceAuthenticationLocalState(new(SourceAuthenticationStatus.Saved), [SessionFile(call.ArgAt<Dictionary<string, string>>(0))]));
        probe.GetCopyableAuthenticationFiles(Arg.Any<Dictionary<string, string>>(), Arg.Any<Dictionary<string, string>>())
            .Returns(call => new[] { SessionFile(call.ArgAt<Dictionary<string, string>>(0)) });
        probe.ReadAuthenticationFilesAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task>>()());
        probe.ReplaceAuthenticationFilesAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task>>()());
        probe.CheckAuthenticationAsync(Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(call => new SourceAuthenticationCheck(File.ReadAllText(SessionFile(call.ArgAt<Dictionary<string, string>>(0))) == "new-session"
                ? SourceAuthenticationStatus.Valid : SourceAuthenticationStatus.Rejected));
        var origin = CreateSource("Образовательный", "Video", root.FullName);
        var target = CreateSource("Игровой", "Video", root.FullName);
        var other = CreateSource("Другая площадка", "Other", root.FullName);
        target.Settings["channel"] = "target-channel";
        var originFile = Path.Combine(origin.Settings["_system_state_path"], "session");
        var targetFile = Path.Combine(target.Settings["_system_state_path"], "session");
        var otherStateFile = Path.Combine(target.Settings["_system_state_path"], "local-state");
        File.WriteAllText(originFile, "new-session");
        File.WriteAllText(targetFile, "previous-session");
        File.WriteAllText(otherStateFile, "keep-target-file");
        try
        {
            ApplyTheme(theme);
            NavigateToPage("sources");
            sources.IsAuthenticationMode = true;
            Settle();
            auth.SetActive(false);
            auth.SetSources([origin, target, other]);
            var originRow = auth.VisibleRows.Single(row => row.Source == origin);
            auth.ToggleDetailsCommand.Execute(originRow);
            Settle();
            var button = Descendants<Button>(_window).Single(control => control.IsVisible
                && AutomationProperties.GetName(control) == originRow.CopyAccessibleName);
            Assert.That(button.IsEnabled, Is.True);

            VisualTestHost.Run(async () =>
            {
                var operation = ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)button.Command).ExecuteAsync(button.CommandParameter);
                Settle();
                try
                {
                    Assert.That(_shell.Modal.Current, Is.TypeOf<CopyAuthenticationViewModel>());
                    var dialog = (CopyAuthenticationViewModel)_shell.Modal.Current!;
                    var selector = Descendants<CopyAuthenticationView>(_window).Single().FindName("TargetSelector") as ComboBox;
                    Assert.That(selector, Is.Not.Null);
                    Assert.That(dialog.ConfirmCommand.CanExecute(null), Is.False);
                    Assert.That(dialog.Targets.Select(row => row.Title), Is.EqualTo(new[] { target.Title }));
                    selector!.SetCurrentValue(Selector.SelectedItemProperty, dialog.Targets.Single());
                    Assert.That(dialog.WillReplaceAuthorization, Is.True);
                    dialog.ConfirmCommand.Execute(null);
                    await operation;
                }
                finally
                {
                    if (_shell.Modal.Current is CopyAuthenticationViewModel dialog)
                    {
                        dialog.CancelCommand.Execute(null);
                        await operation;
                    }
                }
            });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.ReadAllText(targetFile), Is.EqualTo("new-session"));
                Assert.That(File.ReadAllText(originFile), Is.EqualTo("new-session"));
                Assert.That(File.ReadAllText(otherStateFile), Is.EqualTo("keep-target-file"));
                Assert.That(target.Settings["channel"], Is.EqualTo("target-channel"));
                Assert.That(originRow.SignInNotice, Does.Contain("Игровой"));
                Assert.That(auth.VisibleRows.Single(row => row.Source == target).Check.Status, Is.EqualTo(SourceAuthenticationStatus.Valid));
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            }
            _ = probe.Received(1).CheckAuthenticationAsync(
                Arg.Is<Dictionary<string, string>>(settings => settings["_system_state_path"] == target.Settings["_system_state_path"]),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            ApplyTheme(AppThemes.LightKey);
            sources.ViewMode = previousMode;
            auth.SetSources(_services.GetRequiredService<Orcestrator>().GetSources());
            Settle();
            root.Delete(recursive: true);
        }

        Source CreateSource(string title, string platform, string directory)
        {
            var id = Guid.NewGuid().ToString("N");
            return new() { Id = id, TypeId = platform, Type = plugin, Settings = new()
            {
                ["_system_name"] = title,
                ["_system_state_path"] = Directory.CreateDirectory(Path.Combine(directory, id)).FullName,
            }};
        }

        static string SessionFile(Dictionary<string, string> settings) => Path.Combine(settings["_system_state_path"], "session");
    }

    [Test]
    public void Авторизация_ВходИзСтроки_СписокОстаётсяНаМестеИПоказываетОшибку()
    {
        var sources = _services.GetRequiredService<SourcesRelationsViewModel>();
        var previousMode = sources.ViewMode;
        var plugin = Substitute.For<ISourceType, IAuthenticatable>();
        var login = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ((IAuthenticatable)plugin).AuthenticateAsync(
            Arg.Any<Dictionary<string, string>>(), Arg.Any<IAuthUI>(), Arg.Any<CancellationToken>()).Returns(login.Task);
        const string failureText = "Не удалось открыть страницу входа.";

        try
        {
            NavigateToPage("sources");
            sources.IsAuthenticationMode = true;
            Settle();
            var auth = sources.Authentication;
            auth.SetActive(false);
            auth.SetSources(Enumerable.Range(0, 50).Select(index => new Source
            {
                Id = $"test-{index}",
                TypeId = "Тестовая площадка",
                Type = plugin,
                Settings = new() { ["_system_name"] = $"Источник {index:D2}" },
            }));
            Settle();

            var row = auth.VisibleRows[25];
            var list = (ListBox)ViewCapture.Find(_window, "SessionList")!;
            list.ScrollIntoView(row);
            Settle();
            var scroll = Descendants<ScrollViewer>(list).First();
            var top = list.TranslatePoint(new Point(), _window).Y;
            var offset = scroll.VerticalOffset;
            VisualTestHost.Run(async () =>
            {
                var signIn = auth.SignInCommand.ExecuteAsync(row);
                try
                {
                    Settle();
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(auth.IsSigningIn, Is.True);
                        Assert.That(list.TranslatePoint(new Point(), _window).Y, Is.EqualTo(top));
                        Assert.That(scroll.VerticalOffset, Is.EqualTo(offset));
                        Assert.That(row.IsExpanded, Is.False);
                    }
                }
                finally
                {
                    login.TrySetException(new AuthenticationBrowserException(failureText, new InvalidOperationException()));
                    await signIn;
                }
            });

            list.ScrollIntoView(row);
            Settle();
            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(row);
            Assert.That(Descendants<TextBlock>(item).Any(text => text.IsVisible && text.Text == failureText), Is.True);
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
        finally
        {
            sources.ViewMode = previousMode;
            sources.Authentication.SetSources(_services.GetRequiredService<Orcestrator>().GetSources());
            Settle();
        }
    }

    [Test]
    [Explicit("Требуется установленный Chromium для Playwright.")]
    public void Авторизация_YouTubeСоСтарымиCookies_ОткрываетСтраницуПовторногоВхода()
    {
        var plugin = (IAuthenticatable)_services.GetRequiredService<Orcestrator>().GetSourceTypes().Values.Single(type => type.Name == "Youtube");
        var ui = _services.GetRequiredService<WpfAuthUI>();
        var root = Directory.CreateTempSubdirectory("youtube-renew-");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var session = ui.CreateSession(new("Тестовый YouTube", "YouTube", null, false), deadline.Token, _ => { });
        var authFile = Path.Combine(root.FullName, "auth_state");
        const string legacyCookie = "#HttpOnly_.accounts.google.com\tTRUE\t/\tTRUE\t0\t__Host-sample\tsynthetic";
        File.WriteAllText(authFile, legacyCookie);
        try
        {
            VisualTestHost.Run(async () =>
            {
                var signIn = plugin.AuthenticateAsync(new() { ["_system_state_path"] = root.FullName }, session, deadline.Token);
                try
                {
                    while (_shell.Modal.Current is not AuthenticationPromptViewModel { IsReady: true } && !signIn.IsCompleted)
                    {
                        await Task.Delay(30, deadline.Token);
                    }

                    if (signIn.IsCompleted)
                    {
                        await signIn;
                    }

                    Assert.That(_shell.Modal.Current, Is.TypeOf<AuthenticationPromptViewModel>());
                    ((AuthenticationPromptViewModel)_shell.Modal.Current!).CancelCommand.Execute(null);
                    try
                    {
                        await signIn;
                        Assert.Fail("Вход должен быть отменён.");
                    }
                    catch (OperationCanceledException)
                    {
                        Assert.That(deadline.IsCancellationRequested, Is.False);
                    }
                }
                finally
                {
                    if (!signIn.IsCompleted)
                    {
                        await deadline.CancelAsync();
                    }
                }
            });

            Assert.That(File.ReadAllText(authFile), Is.EqualTo(legacyCookie));
            Assert.That(_shell.Modal.HasActive, Is.False);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    [Explicit("Требуется установленный Chromium для Playwright.")]
    public void Авторизация_ОшибкаНавигацииChromium_НеСчитаетсяОтменой()
    {
        var ui = _services.GetRequiredService<WpfAuthUI>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = ui.CreateSession(new("Тест", "Площадка", null, false), deadline.Token, _ => { });

        Exception? failure = null;
        try
        {
            VisualTestHost.Run(() => session.OpenBrowserAsync("http://127.0.0.1:0/"));
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure, Is.TypeOf<AuthenticationBrowserException>());
            Assert.That(failure?.Message, Does.StartWith("Не удалось открыть страницу входа."));
            Assert.That(deadline.IsCancellationRequested, Is.False);
            Assert.That(_shell.Modal.HasActive, Is.False);
        }
    }

    [Test]
    [Explicit("Требуется установленный Chromium для Playwright.")]
    public void Авторизация_СтраницаОткрылась_ДиалогГотовИОтменяется()
    {
        var ui = _services.GetRequiredService<WpfAuthUI>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = ui.CreateSession(new("Тест", "Площадка", null, false), deadline.Token, _ => { });
        VisualTestHost.Run(async () =>
        {
            var signIn = session.OpenBrowserAsync("data:text/html,<title>Вход</title>");
            try
            {
                while (_shell.Modal.Current is not AuthenticationPromptViewModel { IsReady: true } && !signIn.IsCompleted)
                {
                    await Task.Delay(30, deadline.Token);
                }
                Assert.That(_shell.Modal.Current, Is.TypeOf<AuthenticationPromptViewModel>());
                var prompt = (AuthenticationPromptViewModel)_shell.Modal.Current!;
                Assert.That(prompt.ConfirmCommand.CanExecute(null), Is.True);
                prompt.CancelCommand.Execute(null);
                try
                {
                    await signIn;
                    Assert.Fail("Вход должен быть отменён.");
                }
                catch (OperationCanceledException)
                {
                    Assert.That(deadline.IsCancellationRequested, Is.False);
                }
            }
            finally
            {
                if (!signIn.IsCompleted)
                {
                    await deadline.CancelAsync();
                }
            }
        });

        Assert.That(_shell.Modal.HasActive, Is.False);
    }
}
