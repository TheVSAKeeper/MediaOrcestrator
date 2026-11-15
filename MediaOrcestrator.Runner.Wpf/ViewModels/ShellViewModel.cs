using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class ShellViewModel : ShellViewModelBase
{
    private const string TasksDrawerOpenKey = "wpf.shell.tasks_drawer_open";
    private const string NavOperationsExpandedKey = "wpf.shell.nav_operations_expanded";

    [ObservableProperty]
    private bool _isTasksDrawerOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPageRenderStages))]
    private string? _pageRenderDurationText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPageRenderStages))]
    private IReadOnlyList<PageRenderStage>? _pageRenderStages;

    [ObservableProperty]
    private bool _isTarkovBootPlaying;

    private readonly NavigationItem _settingsItem;
    private readonly ISettingsStore _settings;
    private readonly ActionHolder _actionHolder;
    private readonly IDialogService _dialogs;
    private readonly ShellPreferences _preferences;
    private readonly ILogger<ShellViewModel> _logger;
    private bool _suppressPersist;

    private IPageRenderMetric? _renderMetricSource;
    private ICommand? _observedPageRefreshCommand;
    private string? _defaultRenderText;
    private string? _customRenderText;
    private IReadOnlyList<PageRenderStage>? _customRenderStages;

    public ShellViewModel(
        ThemeViewModel theme,
        MediaViewModel media,
        SourcesRelationsViewModel sources,
        AuditViewModel audit,
        TransfersViewModel transfers,
        PublishViewModel publish,
        ChronologyViewModel chronology,
        MaintenanceHubViewModel maintenance,
        LogsViewModel logs,
        DiagnosticsViewModel diagnostics,
        AboutViewModel about,
        SettingsViewModel settings,
        TasksViewModel tasks,
        ModalHostViewModel modal,
        ToastHostViewModel toasts,
        ActionHolder actionHolder,
        IDialogService dialogs,
        ISettingsStore settingsStore,
        CommentsViewModel comments,
        OperationsViewModel operations,
        ShellPreferences preferences,
        AppUpdateViewModel appUpdate,
        ILogger<ShellViewModel> logger)
        : base(modal)
    {
        Theme = theme;
        Theme.PropertyChanged += OnThemePropertyChanged;
        _settings = settingsStore;
        _actionHolder = actionHolder;
        _dialogs = dialogs;
        _preferences = preferences;
        _logger = logger;
        _preferences.PropertyChanged += OnPreferencesPropertyChanged;
        Tasks = tasks;
        Toasts = toasts;
        AppUpdate = appUpdate;
        modal.PropertyChanged += OnModalPropertyChanged;
        PropertyChanged += OnShellPropertyChanged;
        AttachPageRefreshCommand(PageRefreshCommand);

        _actionHolder.Changed += OnActionsChanged;
        RefreshStatusText();

        _suppressPersist = true;
        IsTasksDrawerOpen = bool.TryParse(_settings.GetStringValue(TasksDrawerOpenKey), out var drawerOpen) && drawerOpen;
        _suppressPersist = false;

        _settingsItem = new("Настройки", PackIconLucideKind.Settings, settings, key: "settings");

        Sections.Add(new("Медиа", PackIconLucideKind.Library, media, key: "media"));
        Sections.Add(new("Источники и связи", PackIconLucideKind.Link, sources, key: "sources"));
        Sections.Add(new("Досинк источников", PackIconLucideKind.RefreshCw, audit, key: "audit"));
        Sections.Add(new("Передачи", PackIconLucideKind.ArrowLeftRight, transfers, key: "transfers"));
        Sections.Add(new("Хронология", PackIconLucideKind.Film, chronology, key: "chronology"));
        Sections.Add(new("Публикация", PackIconLucideKind.Upload, publish, key: "publish"));
        Sections.Add(BuildOperationsGroup(operations, IsOperationsExpandedPersisted()));
        Sections.Add(new("Комментарии", PackIconLucideKind.MessageSquare, comments, key: "comments"));
        Sections.Add(new("Обслуживание", PackIconLucideKind.Wrench, maintenance, key: "maintenance") { StartsGroup = true });
        Sections.Add(new("Логи", PackIconLucideKind.ScrollText, logs, key: "logs"));
        Sections.Add(new("Диагностика", PackIconLucideKind.Activity, diagnostics, key: "diagnostics"));
        Sections.Add(new("О программе", PackIconLucideKind.Info, about, key: "about"));

        Selected = Sections[0];
    }

    private NavigationItem BuildOperationsGroup(OperationsViewModel operations, bool isExpanded)
    {
        NavigationItem Leaf(string title, PackIconLucideKind icon, OperationsSubsection subsection, string key) =>
            new(title, icon, operations, activate: () => operations.Selected = subsection, key: key);

        var group = new NavigationItem("Операции", PackIconLucideKind.Wrench, children:
        [
            Leaf("Переименование", PackIconLucideKind.Pencil, OperationsSubsection.Rename, "rename"),
            Leaf("Обложки и превью", PackIconLucideKind.Image, OperationsSubsection.Preview, "preview"),
            Leaf("Слияние дубликатов", PackIconLucideKind.Merge, OperationsSubsection.Merge, "merge"),
            Leaf("Разбор конфликтов", PackIconLucideKind.TriangleAlert, OperationsSubsection.Conflicts, "conflicts"),
        ], key: "operations")
        {
            IsExpanded = isExpanded,
        };

        group.PropertyChanged += OnNavigationGroupPropertyChanged;

        return group;
    }

    private bool IsOperationsExpandedPersisted() =>
        bool.TryParse(_settings.GetStringValue(NavOperationsExpandedKey), out var expanded) && expanded;

    private void OnNavigationGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressPersist
            || e.PropertyName != nameof(NavigationItem.IsExpanded)
            || sender is not NavigationItem group)
        {
            return;
        }

        _settings.SetValue(NavOperationsExpandedKey, group.IsExpanded ? "true" : "false");
    }

    private void ExpandOwningGroup(NavigationItem? item)
    {
        if (item is null)
        {
            return;
        }

        var group = Sections.FirstOrDefault(section => section.IsGroup && section.Children.Contains(item));

        if (group is null || group.IsExpanded)
        {
            return;
        }

        _suppressPersist = true;

        try
        {
            group.IsExpanded = true;
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    public ThemeViewModel Theme { get; }

    public ShellPreferences Preferences => _preferences;

    public override IPageHeader? EffectivePageHeader => _preferences.ShowPageHeader ? CurrentPageHeader : null;

    public TasksViewModel Tasks { get; }

    public AppUpdateViewModel AppUpdate { get; }

    public ToastHostViewModel Toasts { get; }

    public bool HasPageRenderStages => PageRenderStages is { Count: > 0 };

    protected override void OnSelectionChanged(NavigationItem? value)
    {
        ExpandOwningGroup(value);
        AttachRenderMetricSource(value?.Content as IPageRenderMetric);
        BeginMeasurePageRender();
    }

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellPreferences.ShowPageHeader))
        {
            OnPropertyChanged(nameof(EffectivePageHeader));
            OnPropertyChanged(nameof(ContentMargin));
        }
    }

    private void AttachRenderMetricSource(IPageRenderMetric? next)
    {
        if (_renderMetricSource is not null)
        {
            _renderMetricSource.PropertyChanged -= OnRenderMetricSourcePropertyChanged;
        }

        _renderMetricSource = next;
        _customRenderText = next?.RenderDurationText;
        _customRenderStages = next?.RenderStages;

        if (next is not null)
        {
            next.PropertyChanged += OnRenderMetricSourcePropertyChanged;
        }
    }

    private void OnRenderMetricSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;

        if (name is not (null or ""
            or nameof(IPageRenderMetric.RenderDurationText)
            or nameof(IPageRenderMetric.RenderStages)))
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        var source = _renderMetricSource;

        if (source is null)
        {
            return;
        }

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            _customRenderText = source.RenderDurationText;
            _customRenderStages = source.RenderStages;
            UpdatePageRenderDurationText();
            LogRenderMetric(source, name);
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_renderMetricSource, source))
            {
                return;
            }

            _customRenderText = source.RenderDurationText;
            _customRenderStages = source.RenderStages;
            UpdatePageRenderDurationText();
            LogRenderMetric(source, name);
        });
    }

    private void LogRenderMetric(IPageRenderMetric source, string? changedProperty)
    {
        if (changedProperty != nameof(IPageRenderMetric.RenderStages))
        {
            return;
        }

        var stages = source.RenderStages;

        if (stages is null || stages.Count == 0)
        {
            return;
        }

        var pageName = (source as IPageHeader)?.PageTitle ?? source.GetType().Name;
        var stagesText = string.Join("; ", stages.Select(s => $"{s.Name}={s.Value}"));

        _logger.LogInformation(
            "Отрисовка страницы «{Page}»: {Duration}, стадии: {Stages}",
            pageName,
            source.RenderDurationText ?? "–",
            stagesText);
    }

    private void BeginMeasurePageRender()
    {
        _defaultRenderText = null;
        UpdatePageRenderDurationText();

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var selectedAtStart = Selected;

        dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            stopwatch.Stop();

            if (!ReferenceEquals(Selected, selectedAtStart))
            {
                return;
            }

            _defaultRenderText = FormatRenderDuration(stopwatch.Elapsed);
            UpdatePageRenderDurationText();
        }));
    }

    private void UpdatePageRenderDurationText()
    {
        var hasCustom = !string.IsNullOrWhiteSpace(_customRenderText);
        PageRenderDurationText = hasCustom ? _customRenderText : _defaultRenderText;
        PageRenderStages = hasCustom ? _customRenderStages : null;
    }

    private static string FormatRenderDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds < 1000)
        {
            return $"{elapsed.TotalMilliseconds:F0} мс";
        }

        return $"{elapsed.TotalSeconds:F2} с";
    }

    protected override void NavigateToSettings()
    {
        Selected = _settingsItem;
    }

    [RelayCommand]
    private void ToggleTasksDrawer()
    {
        IsTasksDrawerOpen = !IsTasksDrawerOpen;
    }

    [RelayCommand(CanExecute = nameof(CanCloseTasksDrawer))]
    private void CloseTasksDrawer()
    {
        IsTasksDrawerOpen = false;
    }

    private bool CanCloseTasksDrawer() => IsTasksDrawerOpen && !Modal.HasActive;

    [RelayCommand(CanExecute = nameof(CanRefreshActivePage))]
    private void RefreshActivePage()
    {
        if (PageRefreshCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private bool CanRefreshActivePage() => !Modal.HasActive && PageRefreshCommand?.CanExecute(null) == true;

    private void OnModalPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModalHostViewModel.HasActive))
        {
            CloseTasksDrawerCommand.NotifyCanExecuteChanged();
            RefreshActivePageCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(PageRefreshCommand))
        {
            AttachPageRefreshCommand(PageRefreshCommand);
        }
    }

    private void AttachPageRefreshCommand(ICommand? next)
    {
        if (_observedPageRefreshCommand is not null)
        {
            _observedPageRefreshCommand.CanExecuteChanged -= OnObservedPageRefreshCanExecuteChanged;
        }

        _observedPageRefreshCommand = next;

        if (next is not null)
        {
            next.CanExecuteChanged += OnObservedPageRefreshCanExecuteChanged;
        }

        RefreshActivePageCommand.NotifyCanExecuteChanged();
    }

    private void OnObservedPageRefreshCanExecuteChanged(object? sender, EventArgs e)
    {
        RefreshActivePageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsTasksDrawerOpenChanged(bool value)
    {
        CloseTasksDrawerCommand.NotifyCanExecuteChanged();

        if (_suppressPersist)
        {
            return;
        }

        _settings.SetValue(TasksDrawerOpenKey, value ? "true" : "false");
    }

    private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeViewModel.Current) && Theme.Current == AppTheme.Tarkov)
        {
            IsTarkovBootPlaying = true;
        }
    }

    private void OnActionsChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(RefreshStatusText);
    }

    private void RefreshStatusText()
    {
        var snapshot = _actionHolder.Snapshot();

        StatusText = snapshot.Count switch
        {
            0 => "Готов",
            1 => snapshot[0].Name,
            _ => $"Работает {snapshot.Count} задач(и)",
        };
    }

    public override void HandleStartupCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (command.StartsWith(StartupCommandLine.NavCommandPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var target = command[StartupCommandLine.NavCommandPrefix.Length..].Trim();

            if (string.Equals(target, "settings", StringComparison.OrdinalIgnoreCase))
            {
                NavigateToSettings();
                return;
            }

            if (!NavigateByKey(target))
            {
                NavigateToTitle(target);
            }

            return;
        }

        if (command.StartsWith(StartupCommandLine.DrawerCommandPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var drawer = command[StartupCommandLine.DrawerCommandPrefix.Length..].Trim();

            if (string.Equals(drawer, "tasks", StringComparison.OrdinalIgnoreCase))
            {
                IsTasksDrawerOpen = true;
            }
        }
    }

    public void NavigateToTitle(string title)
    {
        TryNavigate(title);
    }

    public bool TryNavigate(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (string.Equals(title, _settingsItem.Title, StringComparison.OrdinalIgnoreCase)
            || string.Equals(title, _settingsItem.Key, StringComparison.OrdinalIgnoreCase))
        {
            NavigateToSettings();
            return true;
        }

        var match = FindByTitle(title);

        if (match is null)
        {
            return false;
        }

        var target = match.IsGroup ? match.Children.FirstOrDefault() : match;

        if (target is null)
        {
            return true;
        }

        ExpandOwningGroup(target);
        Selected = target;

        return true;
    }

    public void NavigateToOperations(OperationsTab tab)
    {
        var group = Sections.FirstOrDefault(s =>
            s.IsGroup && string.Equals(s.Title, "Операции", StringComparison.Ordinal));

        if (group is null)
        {
            return;
        }

        var index = (int)tab;

        if (index >= 0 && index < group.Children.Count)
        {
            var target = group.Children[index];

            ExpandOwningGroup(target);
            Selected = target;
        }
    }

    private NavigationItem? FindByTitle(string title)
    {
        foreach (var section in Sections)
        {
            if (string.Equals(section.Title, title, StringComparison.OrdinalIgnoreCase))
            {
                return section;
            }

            var child = section.Children.FirstOrDefault(c =>
                string.Equals(c.Title, title, StringComparison.OrdinalIgnoreCase));

            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    public override bool RequestClose()
    {
        var active = _actionHolder.ActiveCount;

        if (active == 0)
        {
            return true;
        }

        var message = active == 1
            ? "Сейчас выполняется одна задача. Закрыть приложение? Задача будет отменена."
            : $"Сейчас выполняется задач: {active}. Закрыть приложение? Они будут отменены.";

        return _dialogs.ConfirmWarning("Закрыть приложение?", message);
    }
}
