using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class CopyAuthenticationViewModel(
    SourceAuthenticationRowViewModel origin,
    IReadOnlyList<SourceAuthenticationRowViewModel> targets) : ObservableObject, IDialogViewModel
{
    public event EventHandler<bool>? RequestClose;

    public string OriginText => $"Из «{origin.Title}» · {origin.Platform}";
    public string? OriginWarning { get; } = origin.Check.Status switch
    {
        SourceAuthenticationStatus.Rejected => "Площадка отклонила сессию. Копия может не сработать.",
        SourceAuthenticationStatus.Valid => null,
        _ => "Сессия источника не подтверждена. Копия может не сработать.",
    };
    public IReadOnlyList<SourceAuthenticationRowViewModel> Targets { get; } = targets;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyPropertyChangedFor(nameof(WillReplaceAuthorization))]
    private SourceAuthenticationRowViewModel? _target;

    public bool WillReplaceAuthorization => Target?.HasCopyableAuthentication == true;

    private bool CanConfirm() => Target is not null && Targets.Contains(Target);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => RequestClose?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, false);
}
