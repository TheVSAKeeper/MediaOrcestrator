using KeepShell.ViewModels;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;

namespace MediaOrcestrator.Runner.Wpf.Services;

public sealed partial class WpfAuthUI(IDialogService dialogs, ModalHostViewModel modal, ILogger<WpfAuthUI> logger) : IAuthUI
{
    public IAuthUI CreateSession(SourceAuthenticationPromptContext context, CancellationToken cancellationToken, Action<string> showNotice) =>
        new AuthSession(this, context, cancellationToken, showNotice);

    public Task<string?> PromptInputAsync(string prompt, bool isPassword = false)
    {
        Application.Current.Dispatcher.Invoke(() =>
            dialogs.Info("Авторизация", prompt + "\n\nЗначение нужно ввести в поле настроек источника."));
        throw new AuthenticationDismissedException(CancellationToken.None);
    }

    public Task<string?> OpenBrowserAsync(string url, string? existingStatePath = null) =>
        OpenLegacyBrowserAsync(url, existingStatePath, existingStatePath, null);

    public Task<string?> OpenBrowserAsync(BrowserAuthenticationRequest request) =>
        OpenLegacyBrowserAsync(request.Url, request.OutputStatePath, request.ExistingStatePath, request.VerifyAsync);

    private async Task<string?> OpenLegacyBrowserAsync(string url, string? outputPath, string? existingPath,
        Func<string, CancellationToken, Task<SourceAuthenticationCheck>>? verify)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await browser.NewContextAsync(CreateBrowserOptions(existingPath));
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);

        var confirmed = Application.Current.Dispatcher.Invoke(() => dialogs.Confirm("Сохранить авторизацию?",
            "Войдите в открывшемся браузере, затем сохраните сессию."));
        if (!confirmed || string.IsNullOrEmpty(outputPath) || page.IsClosed)
        {
            return null;
        }

        if (await SaveCandidateAsync(context, outputPath, verify, CancellationToken.None) is { } error)
        {
            throw new InvalidOperationException(error);
        }

        return outputPath;
    }

    public Task ShowMessageAsync(string message)
    {
        Application.Current.Dispatcher.Invoke(() => dialogs.Info("Авторизация", message));
        return Task.CompletedTask;
    }

    private async Task<string?> OpenSessionBrowserAsync(string url, string? outputPath, string? existingPath,
        Func<string, CancellationToken, Task<SourceAuthenticationCheck>>? verify, SourceAuthenticationPromptContext session,
        CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var prompt = new AuthenticationPromptViewModel(
            "Завершите вход в браузере, затем нажмите «Проверить и сохранить».", "Проверить и сохранить",
            sourceTitle: session.Heading, accountHint: session.AccountHint, waitsForBrowser: true,
            pendingPrompt: "Открываем страницу входа в браузере…", logger: logger, cancellationToken: operation.Token);
        var closed = await OpenPromptAsync(prompt, operation.Token);
        using var cancel = operation.Token.Register(() => CloseOnUi(prompt, false));

        async Task ObserveCloseAsync()
        {
            if (!await closed)
            {
                await operation.CancelAsync();
            }
        }

        var observe = ObserveCloseAsync();
        var token = operation.Token;
        var failureMessage = "Не удалось запустить браузер для входа.";
        try
        {
            token.ThrowIfCancellationRequested();
            using var playwright = await Playwright.CreateAsync();
            token.ThrowIfCancellationRequested();
            await using var browser = await LaunchBrowserAsync(playwright);
            logger.LogDebug("Chromium запущен для входа в {SourceTitle} ({Platform})", session.SourceTitle, session.Platform);
            token.ThrowIfCancellationRequested();

            failureMessage = "Не удалось открыть браузерную сессию. Попробуйте снова.";
            await using var context = await browser.NewContextAsync(CreateBrowserOptions(session.ReplaceAccount ? null : existingPath));
            var page = await context.NewPageAsync();
            void BrowserDisconnected(object? sender, IBrowser disconnected) => CloseOnUi(prompt, false);
            void PageOpened(object? sender, IPage opened) => opened.Close += PageClosed;
            void PageClosed(object? sender, IPage closedPage)
            {
                if (context.Pages.All(candidate => candidate.IsClosed))
                {
                    CloseOnUi(prompt, false);
                }
            }

            browser.Disconnected += BrowserDisconnected;
            context.Page += PageOpened;
            page.Close += PageClosed;
            try
            {
                failureMessage = "Не удалось открыть страницу входа. Проверьте подключение и попробуйте снова.";
                await page.GotoAsync(url, new() { Timeout = 30_000, WaitUntil = WaitUntilState.DOMContentLoaded }).WaitAsync(token);
                logger.LogDebug("Страница входа в {SourceTitle} ({Platform}) открыта", session.SourceTitle, session.Platform);

                var committed = false;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    prompt.ValidateAsync = async () =>
                    {
                        var error = await SaveCandidateAsync(context, outputPath, verify, token, () => !closed.IsCompleted);
                        committed = error is null;
                        return error;
                    };
                    prompt.IsReady = true;
                });
                var confirmed = await closed;
                await observe;
                if (prompt.ConfirmCommand.ExecutionTask is { } validation)
                {
                    await validation;
                }

                if (!confirmed && !committed)
                {
                    throw new AuthenticationDismissedException(token);
                }
                return outputPath;
            }
            finally
            {
                browser.Disconnected -= BrowserDisconnected;
                context.Page -= PageOpened;
                foreach (var opened in context.Pages)
                {
                    opened.Close -= PageClosed;
                }
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or OperationCanceledException
            && (closed.IsCompletedSuccessfully && !closed.Result || token.IsCancellationRequested))
        {
            throw new AuthenticationDismissedException(token);
        }
        catch (Exception ex)
        {
            throw new AuthenticationBrowserException(failureMessage, ex);
        }
        finally
        {
            CloseOnUi(prompt, false);
            await observe;
        }
    }

    internal async Task<string?> SaveCandidateAsync(IBrowserContext context, string? outputPath,
        Func<string, CancellationToken, Task<SourceAuthenticationCheck>>? verify, CancellationToken cancellationToken,
        Func<bool>? maySave = null)
    {
        if (outputPath is null)
        {
            return null;
        }

        var candidate = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var state = await context.StorageStateAsync().WaitAsync(cancellationToken);
            await File.WriteAllTextAsync(candidate, state, cancellationToken);
            if (verify is not null)
            {
                using var verifyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                verifyTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                var result = await verify(candidate, verifyTimeout.Token);
                if (result.Status != SourceAuthenticationStatus.Valid)
                {
                    return result.Detail ?? "Площадка ещё не подтвердила вход. Проверьте аккаунт в браузере.";
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (maySave?.Invoke() == false)
            {
                return "Вход отменён.";
            }
            File.Move(candidate, outputPath, overwrite: true);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return "Вход отменён.";
        }
        catch (Exception ex)
        {
            LogFailure(logger, ex, "Не удалось проверить и сохранить сессию браузера");
            return "Не удалось подтвердить новую сессию. Проверьте вход в браузере и попробуйте снова.";
        }
        finally
        {
            try
            {
                File.Delete(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Не удалось удалить временный файл авторизации: {ErrorType}", ex.GetType().Name);
            }
        }
    }

    private async Task RunExternalAsync(Func<CancellationToken, Task> signIn, SourceAuthenticationPromptContext session,
        CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var prompt = new AuthenticationPromptViewModel("Ожидаем завершения входа в браузере. Здесь его можно отменить.", "",
            sourceTitle: session.Heading, accountHint: session.AccountHint, waitsForBrowser: true, showConfirm: false, logger: logger);
        var closed = await OpenPromptAsync(prompt, operation.Token);
        using var cancel = operation.Token.Register(() => CloseOnUi(prompt, false));
        try
        {
            operation.Token.ThrowIfCancellationRequested();
            var signInTask = signIn(operation.Token);
            if (await Task.WhenAny(signInTask, closed) == closed && !await closed)
            {
                await operation.CancelAsync();
            }

            await signInTask;
            operation.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested || closed.IsCompletedSuccessfully && !closed.Result)
        {
            throw new AuthenticationDismissedException(operation.Token);
        }
        finally
        {
            CloseOnUi(prompt, false);
            await closed;
        }
    }

    private async Task<Task<bool>> OpenPromptAsync(AuthenticationPromptViewModel prompt, CancellationToken cancellationToken) =>
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (modal.HasActive)
            {
                throw new AuthenticationPromptUnavailableException();
            }

            return dialogs.ShowAsync(prompt);
        });

    private static void CloseOnUi(AuthenticationPromptViewModel prompt, bool confirmed) =>
        Application.Current.Dispatcher.BeginInvoke(new Action(() => prompt.Close(confirmed)));

    private static Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright) =>
        playwright.Chromium.LaunchAsync(new() { Headless = false, Timeout = 15_000 });

    internal static BrowserNewContextOptions CreateBrowserOptions(string? existingPath) =>
        new() { StorageStatePath = File.Exists(existingPath) ? existingPath : null };

    internal static void LogFailure(ILogger logger, Exception exception, string operation)
    {
        var message = exception.Message;
        var networkError = BrowserNetworkError().Match(message);
        var reason = exception switch
        {
            TimeoutException or OperationCanceledException => "Истекло время ожидания",
            PlaywrightException when message.Contains("Invalid cookie fields", StringComparison.OrdinalIgnoreCase) =>
                "Network.setCookies: Invalid cookie fields",
            PlaywrightException when message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) =>
                "Chromium не установлен",
            PlaywrightException when message.Contains("Host system is missing dependencies", StringComparison.OrdinalIgnoreCase) =>
                "Не хватает системных зависимостей Chromium",
            PlaywrightException when networkError.Success => networkError.Value,
            UnauthorizedAccessException => "Отказано в доступе",
            IOException => "Сбой чтения или записи",
            _ => "Сбой операции",
        };
        logger.LogWarning("{Operation}. Причина: {Reason}. {ErrorType} (HRESULT {ErrorCode}). {StackTrace}",
            operation, reason, exception.GetType().Name, exception.HResult, exception.StackTrace);
    }

    [GeneratedRegex(@"\bnet::ERR_[A-Z0-9_]{1,64}\b", RegexOptions.CultureInvariant)]
    private static partial Regex BrowserNetworkError();

    private sealed class AuthSession(WpfAuthUI owner, SourceAuthenticationPromptContext session,
        CancellationToken cancellationToken, Action<string> showNotice) : IAuthUI
    {
        public bool ReplaceAccount => session.ReplaceAccount;

        public async Task<string?> PromptInputAsync(string prompt, bool isPassword = false)
        {
            var input = new AuthenticationPromptViewModel(prompt, "Продолжить", hasInput: true, isPassword,
                session.Heading, session.AccountHint, cancellationToken: cancellationToken);
            var result = await owner.OpenPromptAsync(input, cancellationToken);
            using var cancel = cancellationToken.Register(() => CloseOnUi(input, false));
            if (!await result)
            {
                throw new AuthenticationDismissedException(cancellationToken);
            }

            return input.Value;
        }

        public Task<string?> OpenBrowserAsync(string url, string? existingStatePath = null) =>
            owner.OpenSessionBrowserAsync(url, existingStatePath, existingStatePath, null, session, cancellationToken);

        public Task<string?> OpenBrowserAsync(BrowserAuthenticationRequest request) =>
            owner.OpenSessionBrowserAsync(request.Url, request.OutputStatePath, request.ExistingStatePath,
                request.VerifyAsync, session, cancellationToken);

        public Task RunExternalSignInAsync(Func<CancellationToken, Task> signIn, CancellationToken token) =>
            owner.RunExternalAsync(signIn, session, token);

        public Task ShowMessageAsync(string message)
        {
            showNotice(message);
            return Task.CompletedTask;
        }
    }
}

public sealed record SourceAuthenticationPromptContext(string SourceTitle, string Platform, string? Account, bool ReplaceAccount)
{
    public string Heading => $"{SourceTitle} ({Platform})";
    public string? AccountHint => string.IsNullOrWhiteSpace(Account) ? null : ReplaceAccount
        ? $"Текущий аккаунт · {Account}. В браузере выберите другой."
        : $"Войдите в аккаунт {Account}.";
}

internal sealed class AuthenticationPromptUnavailableException()
    : InvalidOperationException("Закройте открытое окно приложения и попробуйте снова.")
{
}

internal sealed class AuthenticationBrowserException(string message, Exception cause) : InvalidOperationException(message, cause);

internal sealed class AuthenticationDismissedException(CancellationToken cancellationToken) : OperationCanceledException(cancellationToken);
