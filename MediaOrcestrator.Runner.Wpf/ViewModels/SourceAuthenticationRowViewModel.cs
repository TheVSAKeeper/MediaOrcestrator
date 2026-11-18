using CommunityToolkit.Mvvm.ComponentModel;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class SourceAuthenticationRowViewModel(Source source, SourceAuthenticationState state) : ObservableObject
{
    private readonly HashSet<string?> _pendingMethods = [];

    public Source Source { get; private set; } = source;
    internal SourceAuthenticationState State { get; private set; } = state;
    internal IReadOnlyCollection<string?> PendingMethods => _pendingMethods;
    public string Title => Source.Title;
    public string Platform => string.IsNullOrWhiteSpace(Source.Type.DisplayName) ? Source.TypeId : Source.Type.DisplayName;
    public bool IsDisabled => Source.IsDisable;
    public string PlatformText => IsDisabled ? $"{Platform} · выключен" : Platform;
    public string AccessibleName => $"{Title}, {PlatformText}, {AccountText}, {StatusBadge.Text}";
    public string CheckAccessibleName => $"Проверить сессию · {Title} ({Platform})";
    public string SettingsAccessibleName => $"Настройки · {Title} ({Platform})";
    public string SignInAccessibleName => $"{PrimarySignInText} · {Title} ({Platform})";
    public string DetailsAccessibleName => $"{(IsExpanded ? "Свернуть детали" : "Раскрыть детали")} · {Title} ({Platform})";
    public PackIconLucideKind DetailsIcon => IsExpanded ? PackIconLucideKind.ChevronDown : PackIconLucideKind.ChevronRight;

    public string? AccountText
    {
        get
        {
            var accounts = Methods.Where(method => !string.IsNullOrWhiteSpace(method.Method.Account)).ToArray();
            if (accounts.Length == 0)
            {
                return FormatAccount(Check.Account, Check.Status);
            }

            var names = accounts.Select(method => method.Method.Account).Distinct().ToArray();
            return names.Length == 1
                ? FormatAccount(names[0], accounts.Any(method => method.Method.Status == SourceAuthenticationStatus.Valid)
                    ? SourceAuthenticationStatus.Valid : SourceAuthenticationStatus.Saved)
                : string.Join(" · ", accounts.Select(method => $"{method.Method.Title} – {method.Method.Account}"));
        }
    }

    public bool CanSignIn => Source.Type is IAuthenticatable && !NeedsSettings;
    public bool NeedsSettings => State.Local.Status == SourceAuthenticationStatus.SettingsRequired
        || Check.Status == SourceAuthenticationStatus.SettingsRequired;
    public bool NeedsSignIn => CanSignIn && (DefaultMethodId is not null
        || !IsAwaiting(null) && Check.Methods is null
            && Check.Status is SourceAuthenticationStatus.Missing or SourceAuthenticationStatus.Rejected);
    public bool ShowInlineSignIn => !IsDisabled && !IsExpanded && NeedsSignIn && !CanRetryCheck;
    public bool ShowInlineSettings => !IsDisabled && !IsExpanded && NeedsSettings;
    public bool ShowInlineRetry => !IsDisabled && !IsExpanded && CanRetryCheck;
    public bool ShowDetailsSignIn => CanSignIn && !IsAwaiting(null) && Methods.Count == 0;
    public bool CanRetryCheck => !NeedsSettings && (AwaitingVerification
        || !NeedsSignIn && Check.Status == SourceAuthenticationStatus.CheckFailed);
    public bool AwaitingVerification => _pendingMethods.Count > 0;
    public bool IsUnchecked => IsChecking || State.LastCheck is null
        && State.Local.Status is SourceAuthenticationStatus.None or SourceAuthenticationStatus.Saved;
    public bool NeedsAttention => !IsDisabled && (AwaitingVerification || Check.Status is SourceAuthenticationStatus.Missing
        or SourceAuthenticationStatus.Saved or SourceAuthenticationStatus.Rejected or SourceAuthenticationStatus.CheckFailed
        or SourceAuthenticationStatus.SettingsRequired);

    public SourceAuthenticationCheck Check => State.LastCheck ?? State.Local;
    public DateTime? CheckedAt => State.CheckedAt?.ToLocalTime();
    public DateTime? LastSuccessfulCheck => State.LastConfirmedAt?.ToLocalTime();
    public IReadOnlyList<AuthenticationMethodRow> Methods => (Check.Methods ?? State.Local.Methods)?
        .Select(method => new AuthenticationMethodRow(this, method, IsAwaiting(method.Id))).ToArray() ?? [];
    internal IEnumerable<AuthenticationMethodRow> MethodsNeedingSignIn => Methods.Where(method => method.CanSignIn
        && method.Method.Status is SourceAuthenticationStatus.Missing or SourceAuthenticationStatus.Rejected);
    public string? DefaultMethodId => MethodsNeedingSignIn.OrderByDescending(method => method.Method.Status == SourceAuthenticationStatus.Rejected)
        .FirstOrDefault()?.Method.Id;
    internal SourceAuthenticationStatus StatusFor(string? methodId) =>
        methodId is null ? Check.Status : Check.Methods?.FirstOrDefault(method => method.Id == methodId)?.Status
            ?? (Check.Status == SourceAuthenticationStatus.CheckFailed ? Check.Status : SourceAuthenticationStatus.Saved);

    public string SignInText => Check.Status == SourceAuthenticationStatus.Rejected || State.Local.Status == SourceAuthenticationStatus.Saved
        ? "Войти заново"
        : "Войти";
    public string PrimarySignInText => MethodsNeedingSignIn.FirstOrDefault(method => method.Method.Id == DefaultMethodId) is { } method
        ? $"{method.ActionText} · {method.Method.Title}"
        : SignInText;
    public bool CanSwitchAccount => Check.Status == SourceAuthenticationStatus.Valid
        && Source.Type is ISourceAuthenticationProbe { CanSwitchAccount: true };
    public string DetailsSignInText => CanSwitchAccount ? "Сменить аккаунт"
        : Check.Status == SourceAuthenticationStatus.Valid ? "Обновить вход" : SignInText;
    public string DetailsSignInAccessibleName => $"{DetailsSignInText} · {Title} ({Platform})";

    public AuthenticationBadge StatusBadge => IsChecking ? new("Проверяем…", SourceAuthenticationStatus.None)
        : AwaitingVerification && Check.Status != SourceAuthenticationStatus.CheckFailed ? new("Ожидает проверки", SourceAuthenticationStatus.None)
        : new(Describe(Check.Status), Check.Status);
    public string StatusText => StatusBadge.Text;

    public string LocalText => State.Local.Status switch
    {
        SourceAuthenticationStatus.Missing => "Данных входа нет",
        SourceAuthenticationStatus.Saved when Check.Status == SourceAuthenticationStatus.Rejected => "Площадка отклонила сохранённую сессию",
        SourceAuthenticationStatus.Saved => "Данные входа сохранены",
        SourceAuthenticationStatus.SettingsRequired => "Для входа нужны настройки",
        _ => "Наличие данных входа уточняется",
    };
    public bool ShowLocalText => Methods.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusBadge))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    [NotifyPropertyChangedFor(nameof(IsUnchecked))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInlineSignIn))]
    [NotifyPropertyChangedFor(nameof(ShowInlineSettings))]
    [NotifyPropertyChangedFor(nameof(ShowInlineRetry))]
    [NotifyPropertyChangedFor(nameof(DetailsAccessibleName))]
    [NotifyPropertyChangedFor(nameof(DetailsIcon))]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isQueueCurrent;

    [ObservableProperty]
    private string? _signInError;

    [ObservableProperty]
    private string? _signInNotice;

    internal bool IsAwaiting(string? methodId) => _pendingMethods.Contains(methodId);

    internal void SetAwaiting(string? methodId, bool pending)
    {
        if (pending ? _pendingMethods.Add(methodId) : _pendingMethods.Remove(methodId))
        {
            OnPropertyChanged(string.Empty);
        }
    }

    internal void Refresh(Source updated, SourceAuthenticationState updatedState)
    {
        Source = updated;
        State = updatedState;
        OnPropertyChanged(string.Empty);
    }

    internal string? AccountFor(string? methodId) => methodId is null
        ? Check.Account
        : Methods.FirstOrDefault(method => method.Method.Id == methodId)?.Method.Account;

    internal static string? FormatAccount(string? account, SourceAuthenticationStatus status) =>
        string.IsNullOrWhiteSpace(account) ? null
            : $"{(status == SourceAuthenticationStatus.Valid ? "Аккаунт" : "Последний аккаунт")} · {account}";

    internal static string Describe(SourceAuthenticationStatus status) => status switch
    {
        SourceAuthenticationStatus.Missing => "Нет данных входа",
        SourceAuthenticationStatus.Saved => "Вход не подтверждён",
        SourceAuthenticationStatus.Valid => "Сессия действует",
        SourceAuthenticationStatus.Rejected => "Нужен повторный вход",
        SourceAuthenticationStatus.CheckFailed => "Ошибка проверки",
        SourceAuthenticationStatus.SettingsRequired => "Нужны настройки",
        _ => "Ожидает проверки",
    };
}

public sealed record AuthenticationMethodRow(SourceAuthenticationRowViewModel Row, SourceAuthenticationMethod Method, bool IsAwaiting)
{
    public bool CanSignIn => Method.CanSignIn && !IsAwaiting;
    public bool ShowAction => CanSignIn && Method.Status is SourceAuthenticationStatus.Valid
        or SourceAuthenticationStatus.Missing or SourceAuthenticationStatus.Rejected;
    public bool ChangesAccount => Method.Status == SourceAuthenticationStatus.Valid;
    public string StatusText => IsAwaiting ? "Ожидает проверки" : SourceAuthenticationRowViewModel.Describe(Method.Status);
    public string ActionText => Method.Status switch
    {
        SourceAuthenticationStatus.Valid => "Сменить аккаунт",
        SourceAuthenticationStatus.Missing => "Войти",
        _ => "Войти заново",
    };
    public string AccessibleAction => $"{ActionText} · {Row.Title} ({Method.Title})";
    public string? AccountText => SourceAuthenticationRowViewModel.FormatAccount(Method.Account, Method.Status);
    public string CompactText => $"{Method.Title} · {(ChangesAccount ? "вход подтверждён" : StatusText)}";
    public AuthenticationBadge Badge => new(StatusText, IsAwaiting ? SourceAuthenticationStatus.None : Method.Status);
}

public sealed record AuthenticationBadge(string Text, SourceAuthenticationStatus Status)
{
    public PackIconLucideKind Icon => Status switch
    {
        SourceAuthenticationStatus.Valid => PackIconLucideKind.CircleCheck,
        SourceAuthenticationStatus.Rejected => PackIconLucideKind.KeyRound,
        SourceAuthenticationStatus.CheckFailed => PackIconLucideKind.TriangleAlert,
        SourceAuthenticationStatus.SettingsRequired => PackIconLucideKind.Settings,
        SourceAuthenticationStatus.Missing => PackIconLucideKind.Key,
        _ => PackIconLucideKind.Clock,
    };
}
