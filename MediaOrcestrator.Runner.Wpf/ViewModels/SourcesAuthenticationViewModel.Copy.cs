using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Modules;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class SourcesAuthenticationViewModel
{
    private bool _copyingAuthentication;

    [ObservableProperty]
    private bool _isReplacingAuthentication;

    private bool CanCopyAuthentication(SourceAuthenticationRowViewModel? origin) =>
        origin is not null && _rows.Contains(origin) && origin.HasCopyableAuthentication
        && CanOpenSourceSettings && !HasQueue
        && _rows.Any(row => row != origin && row.Source.TypeId.Equals(origin.Source.TypeId, StringComparison.OrdinalIgnoreCase));

    [RelayCommand(CanExecute = nameof(CanCopyAuthentication), IncludeCancelCommand = true)]
    private async Task CopyAuthenticationAsync(SourceAuthenticationRowViewModel? origin, CancellationToken cancellationToken)
    {
        if (origin is null || !CanCopyAuthentication(origin))
        {
            return;
        }

        _copyingAuthentication = true;
        origin.SignInError = null;
        origin.SignInNotice = null;
        NotifyResults();
        try
        {
            var targets = _rows.Where(row => authentication.CanCopyAuthentication(origin.Source, row.Source)).ToArray();
            if (targets.Length == 0)
            {
                origin.SignInError = "Нет источника с совместимыми настройками входа.";
                return;
            }

            var dialog = new CopyAuthenticationViewModel(origin, targets);
            if (!await dialogs.ShowAsync(dialog) || dialog.Target is not { } target)
            {
                return;
            }

            IsReplacingAuthentication = true;
            try
            {
                await PauseForEditAsync(origin.Source.Id);
                await PauseForEditAsync(target.Source.Id);
                if (!_rows.Contains(origin) || !_rows.Contains(target))
                {
                    ShowCopyError(origin, "Список источников изменился. Выберите получателя заново.");
                    return;
                }

                await authentication.CopyAuthenticationAsync(origin.Source, target.Source, cancellationToken);
                target.Refresh(target.Source, authentication.ReadState(target.Source));
                foreach (var methodId in target.PendingMethods.ToArray())
                {
                    target.SetAwaiting(methodId, false);
                }
                target.SignInError = null;
                target.SignInNotice = null;
                var message = $"Данные входа скопированы в «{target.Title}».";
                if (VisibleRows.Contains(origin))
                {
                    origin.SignInNotice = message;
                }
                else
                {
                    dialogs.Info("Копирование авторизации", message);
                }
            }
            finally
            {
                IsReplacingAuthentication = false;
                ResumeAfterEdit(origin.Source.Id);
                ResumeAfterEdit(target.Source.Id);
            }

            await BeginCheck(target, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (VisibleRows.Contains(origin))
            {
                origin.SignInNotice = "Копирование отменено.";
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Не удалось скопировать авторизацию из источника {SourceId}: {ErrorType}",
                origin.Source.Id, ex.GetType().Name);
            ShowCopyError(origin, "Не удалось скопировать данные входа. Проверьте состояние источников и попробуйте снова.");
        }
        finally
        {
            _copyingAuthentication = false;
            NotifyResults();
        }
    }

    private void ShowCopyError(SourceAuthenticationRowViewModel origin, string message)
    {
        if (VisibleRows.Contains(origin))
        {
            origin.SignInError = message;
        }
        else
        {
            dialogs.Warning("Копирование авторизации", message);
        }
    }
}
