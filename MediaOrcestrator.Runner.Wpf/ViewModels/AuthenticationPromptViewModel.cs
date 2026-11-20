using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class AuthenticationPromptViewModel(
    string prompt,
    string confirmText,
    bool hasInput = false,
    bool isPassword = false,
    string? sourceTitle = null,
    string? accountHint = null,
    bool waitsForBrowser = false,
    bool showConfirm = true,
    string? pendingPrompt = null,
    ILogger? logger = null,
    CancellationToken cancellationToken = default) : ObservableObject, IDialogViewModel
{
    public string Heading { get; } = sourceTitle ?? "Авторизация";
    public string? AccountHint { get; } = accountHint;
    public string Prompt { get; } = prompt;
    public string ConfirmText { get; } = confirmText;
    public bool ShowTextInput => hasInput && !isPassword;
    public bool ShowPasswordInput => hasInput && isPassword;
    public bool ShowConfirm => showConfirm;
    public bool ConfirmOnEnter => hasInput;

    internal Func<Task<string?>>? ValidateAsync { get; set; }

    public event EventHandler<bool>? RequestClose;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string? _value;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    [NotifyPropertyChangedFor(nameof(PromptText))]
    private bool _isReady = !waitsForBrowser;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    [NotifyPropertyChangedFor(nameof(PromptText))]
    private bool _isValidating;

    public bool ShowProgress => !IsReady || IsValidating;
    public string PromptText => IsValidating ? "Проверяем сессию…" : !IsReady ? pendingPrompt ?? Prompt : Prompt;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (ValidateAsync is not null)
        {
            IsValidating = true;
            ValidationMessage = null;
            try
            {
                ValidationMessage = await ValidateAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (logger is not null)
                {
                    WpfAuthUI.LogFailure(logger, ex, "Не удалось проверить новую сессию");
                }
                ValidationMessage = "Не удалось проверить новую сессию. Попробуйте ещё раз.";
            }
            finally
            {
                IsValidating = false;
            }
            if (ValidationMessage is not null)
            {
                return;
            }
        }

        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    internal void Close(bool confirmed) => RequestClose?.Invoke(this, confirmed);

    private bool CanConfirm() => showConfirm && IsReady && (!hasInput || !string.IsNullOrWhiteSpace(Value));
}
