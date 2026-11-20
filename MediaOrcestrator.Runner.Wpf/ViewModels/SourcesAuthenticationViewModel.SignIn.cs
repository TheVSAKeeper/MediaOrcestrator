using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class SourcesAuthenticationViewModel
{
    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private Task SignInAsync(SourceAuthenticationRowViewModel? row, CancellationToken cancellationToken) =>
        row is null ? Task.CompletedTask : SignInCoreAsync(row, row.DefaultMethodId, null, cancellationToken,
            replaceAccount: row.CanSwitchAccount);

    [RelayCommand(CanExecute = nameof(CanSignInWithMethod))]
    private Task SignInWithMethodAsync(AuthenticationMethodRow? method, CancellationToken cancellationToken) =>
        method is null ? Task.CompletedTask : SignInCoreAsync(method.Row, method.Method.Id, null, cancellationToken, method.ChangesAccount);

    [RelayCommand(CanExecute = nameof(CanSwitchAccount))]
    private Task SwitchAccountAsync(SourceAuthenticationRowViewModel? row, CancellationToken cancellationToken) =>
        row is null ? Task.CompletedTask : SignInCoreAsync(row, null, null, cancellationToken, replaceAccount: true);

    [RelayCommand(CanExecute = nameof(CanSwitchMethodAccount))]
    private Task SwitchMethodAccountAsync(AuthenticationMethodRow? method, CancellationToken cancellationToken) =>
        method is null ? Task.CompletedTask : SignInCoreAsync(method.Row, method.Method.Id, null, cancellationToken, replaceAccount: true);

    [RelayCommand]
    private void CancelSignIn()
    {
        SignInCommand.Cancel();
        SignInWithMethodCommand.Cancel();
        SwitchAccountCommand.Cancel();
        SwitchMethodAccountCommand.Cancel();
    }

    [RelayCommand]
    private static void DismissNotice(SourceAuthenticationRowViewModel? row)
    {
        if (row is not null)
        {
            row.SignInNotice = null;
        }
    }

    private async Task SignInCoreAsync(SourceAuthenticationRowViewModel row, string? methodId, QueueStep? step,
        CancellationToken cancellationToken, bool replaceAccount = false)
    {
        if (_signingIn is not null || !_rows.Contains(row))
        {
            return;
        }

        BeginInteraction(row);
        var saved = false;
        try
        {
            await WaitForCheckAsync(row, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_rows.Contains(row) || (step is not null && CurrentStep != step))
            {
                return;
            }

            var source = row.Source;
            var local = authentication.Inspect(source);
            if (local.Check.Status == SourceAuthenticationStatus.SettingsRequired
                || methodId is not null && local.Check.Methods?.Any(method => method.Id == methodId && !method.CanSignIn) == true)
            {
                row.Refresh(row.Source, row.State with { Local = local.Check, LastCheck = null });
                row.SignInError = local.Check.Detail;
                return;
            }

            string? notice = null;
            var context = new SourceAuthenticationPromptContext(row.Title, row.Platform, row.AccountFor(methodId), replaceAccount);
            await authentication.SaveSignInAsync(source, createAuthUi(context, cancellationToken, message => notice = message),
                methodId, cancellationToken);
            saved = true;
            if (!_rows.Contains(row))
            {
                return;
            }

            row.Refresh(source, authentication.ReadState(source));
            row.SignInNotice = notice;
            row.SetAwaiting(methodId, true);
            if (step is not null)
            {
                step.Saved = true;
                if (CurrentStep != step)
                {
                    step.Outcome = "данные сохранены, вход не подтверждён";
                    return;
                }
            }

            NotifyResults();
            await VerifySignInAsync(row, cancellationToken);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested || ex is AuthenticationDismissedException)
        {
            row.SignInError = saved ? "Данные сохранены. Проверку можно повторить." : "Вход отменён.";
        }
        catch (AuthenticationPromptUnavailableException ex)
        {
            row.SignInError = ex.Message;
        }
        catch (AuthenticationBrowserException ex)
        {
            WpfAuthUI.LogFailure(logger, ex.InnerException!, $"Вход в источник {row.Source.Id} ({methodId}): {ex.Message}");
            row.SignInError = ex.Message;
        }
        catch (OperationCanceledException ex)
        {
            WpfAuthUI.LogFailure(logger, ex, $"Вход в источник {row.Source.Id} ({methodId})");
            row.SignInError = "Истекло время ожидания ответа. Попробуйте войти ещё раз.";
        }
        catch (Exception ex)
        {
            logger.LogWarning("Вход в источник {SourceId} завершился ошибкой {ErrorType}", row.Source.Id, ex.GetType().Name);
            row.SignInError = "Не удалось завершить вход. Проверьте настройки источника и попробуйте снова.";
        }
        finally
        {
            EndInteraction();
        }
    }

    private async Task VerifySignInAsync(SourceAuthenticationRowViewModel row, CancellationToken token)
    {
        ProgressText = $"Проверка сохранённой сессии · {row.Title} ({row.Platform})";
        row.IsChecking = true;
        try
        {
            await CheckRowAsync(row, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            row.SignInError = "Данные сохранены. Проверку можно повторить.";
        }
        finally
        {
            row.IsChecking = false;
            NotifyResults();
        }
    }

    private void BeginInteraction(SourceAuthenticationRowViewModel row)
    {
        _signingIn = row;
        if (HasQueue)
        {
            SetExpanded(null);
        }
        row.SignInError = null;
        row.SignInNotice = null;
        ProgressText = $"Вход · {row.Title} ({row.Platform})";
        NotifyResults();
    }

    private void EndInteraction()
    {
        if (_signingIn is { SignInError: not null } row && row == QueueCurrent)
        {
            SetExpanded(row);
        }

        var rowWasReplaced = _signingIn is not null && !_rows.Contains(_signingIn);
        _signingIn = null;
        ProgressText = null;
        NotifyResults();
        if (rowWasReplaced)
        {
            ScheduleChecks();
        }
    }

    private bool CanSignIn(SourceAuthenticationRowViewModel? row) =>
        row is not null && _rows.Contains(row) && row.CanSignIn && CanOpenSourceSettings && !HasQueue;
    private bool CanSignInWithMethod(AuthenticationMethodRow? method) =>
        method is { CanSignIn: true } && !method.Row.IsAwaiting(method.Method.Id) && CanSignIn(method.Row);
    private bool CanSwitchAccount(SourceAuthenticationRowViewModel? row) => row?.ShowAlternativeSwitch == true && CanSignIn(row);
    private bool CanSwitchMethodAccount(AuthenticationMethodRow? method) => method?.ShowAlternativeSwitch == true && CanSignInWithMethod(method);
}
