using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class ActionRowViewModel : ObservableObject, IDisposable
{
    private readonly ActionHolder.RunningAction _action;
    private readonly Action _markDirty;
    private readonly Action<ActionRowViewModel> _onExpandedChanged;
    private readonly IUiDispatcher _dispatcher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChevronIcon))]
    private bool _hasChildren;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChevronIcon))]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isVisible = true;

    public ActionRowViewModel(
        ActionHolder.RunningAction action,
        Action markDirty,
        Action<ActionRowViewModel> onExpandedChanged,
        IUiDispatcher dispatcher)
    {
        _action = action;
        _markDirty = markDirty;
        _onExpandedChanged = onExpandedChanged;
        _dispatcher = dispatcher;
        _action.Changed += OnActionChanged;
    }

    public Guid Id => _action.Id;

    public string Name => _action.Name;

    public string Status => _action.Status;

    public string Subtitle
    {
        get
        {
            var subtitle = _action.Subtitle;
            var state = _action.State;

            if (state is ActionState.Succeeded or ActionState.Failed or ActionState.Cancelled)
            {
                var duration = FormatDuration(_action.Duration);
                return subtitle.Length > 0 ? $"{subtitle} · {duration}" : duration;
            }

            return subtitle;
        }
    }

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    public DateTime StartedAt => _action.StartedAt;

    public DateTime? FinishedAt => _action.FinishedAt;

    public string TimeBadge
    {
        get
        {
            var started = _action.StartedAt.ToString("HH:mm:ss");
            var finished = _action.FinishedAt;

            return finished is null ? started : $"{started} → {finished.Value:HH:mm:ss}";
        }
    }

    public int Depth => _action.Depth;

    public bool IsRunning => _action.State == ActionState.Running;

    public bool IsTerminal => _action.State is ActionState.Succeeded or ActionState.Failed or ActionState.Cancelled;

    public bool IsFailed => _action.State == ActionState.Failed;

    public bool HasDetails => IsFailed && !string.IsNullOrEmpty(_action.Error);

    public bool IsIndeterminate => IsRunning && _action.ProgressMax <= 0;

    public int ProgressMax => Math.Max(_action.ProgressMax, 1);

    public int ProgressValue
    {
        get
        {
            var max = _action.ProgressMax;

            if (max <= 0)
            {
                return 0;
            }

            return Math.Clamp(_action.ProgressValue, 0, max);
        }
    }

    public string ProgressLabel
    {
        get
        {
            var max = _action.ProgressMax;

            if (max <= 0)
            {
                var count = _action.ProgressValue;
                return count > 0 ? count.ToString() : string.Empty;
            }

            var value = Math.Clamp(_action.ProgressValue, 0, max);
            return max == 100 ? $"{value} %" : $"{value} / {max}";
        }
    }

    public bool HasProgressLabel => !string.IsNullOrEmpty(ProgressLabel);

    public string? Error => _action.Error;

    public string StateText => _action.State switch
    {
        ActionState.Succeeded => "Готово",
        ActionState.Failed => "Ошибка",
        ActionState.Cancelled => "Отменено",
        _ => string.Empty,
    };

    public string CompactSubline
    {
        get
        {
            var parts = new List<string>(4);

            if (!string.IsNullOrEmpty(Status))
            {
                parts.Add(Status);
            }

            var progress = ProgressLabel;

            if (!string.IsNullOrEmpty(progress))
            {
                parts.Add(progress);
            }

            var subtitle = Subtitle;

            if (!string.IsNullOrEmpty(subtitle))
            {
                parts.Add(subtitle);
            }

            if (!string.IsNullOrEmpty(StateText))
            {
                parts.Add(StateText);
            }

            return string.Join(" · ", parts);
        }
    }

    public ActionAccentKind Accent => _action.State switch
    {
        ActionState.Succeeded => ActionAccentKind.Succeeded,
        ActionState.Failed => ActionAccentKind.Failed,
        ActionState.Cancelled => ActionAccentKind.Cancelled,
        _ => ActionAccentKind.Running,
    };

    public PackIconLucideKind ChevronIcon => IsExpanded ? PackIconLucideKind.ChevronDown : PackIconLucideKind.ChevronRight;

    [RelayCommand]
    private void Cancel()
    {
        _action.Cancel();
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        if (!HasChildren)
        {
            return;
        }

        IsExpanded = !IsExpanded;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        _onExpandedChanged(this);
    }

    public void Dispose()
    {
        _action.Changed -= OnActionChanged;
    }

    private void OnActionChanged(object? sender, EventArgs e)
    {
        _dispatcher.Invoke(NotifyChanged);
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(string.Empty);
        _markDirty();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var totalSeconds = (int)Math.Round(duration.TotalSeconds);

        if (totalSeconds < 0)
        {
            totalSeconds = 0;
        }

        if (totalSeconds < 60)
        {
            return $"за {totalSeconds} с";
        }

        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;

        if (minutes < 60)
        {
            return seconds == 0
                ? $"за {minutes} мин"
                : $"за {minutes} мин {seconds} с";
        }

        var hours = minutes / 60;
        minutes %= 60;

        return minutes == 0
            ? $"за {hours} ч"
            : $"за {hours} ч {minutes} мин";
    }
}
