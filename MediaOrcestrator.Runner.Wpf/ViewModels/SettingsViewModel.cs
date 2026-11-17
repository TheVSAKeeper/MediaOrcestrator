using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Bootstrap;
using KeepShell.Mcp;
using KeepShell.Services.Platform;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.ViewModels.Settings;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public partial class SettingsViewModel : ObservableObject, IPageHeader
{
    public string PageTitle => "Настройки";

    public string? PageDescription => "Пути хранения, тема оформления, уведомления, фоновая синхронизация, обновления и MCP-сервер.";
    public const string SelectedSectionKey = "wpf.settings.section";

    private readonly ISettingsStore _store;
    private readonly IDialogService _dialogs;
    private readonly IAppLifetime _lifetime;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IClipboardService _clipboard;
    private readonly IShellLauncher _shellLauncher;
    private readonly McpServerOptions _mcpOptions;
    private readonly ToastHostViewModel _toasts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedRecently))]
    private string? _statusMessage;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(McpConnectSnippet))]
    private int _selectedMcpFormatIndex;

    public SettingsViewModel(
        ISettingsStore store,
        Paths paths,
        IDialogService dialogs,
        IAppLifetime lifetime,
        ILogger<SettingsViewModel> logger,
        ShellPreferences preferences,
        ThemeViewModel theme,
        UpdatePreferences updates,
        AppUpdateViewModel appUpdate,
        McpPreferences mcp,
        McpServerHost mcpServer,
        McpServerOptions mcpOptions,
        IClipboardService clipboard,
        IShellLauncher shellLauncher,
        IFilePicker filePicker,
        ToastHostViewModel toasts)
    {
        _store = store;
        _dialogs = dialogs;
        _lifetime = lifetime;
        _logger = logger;
        _clipboard = clipboard;
        _shellLauncher = shellLauncher;
        _mcpOptions = mcpOptions;
        _toasts = toasts;
        Preferences = preferences;
        Theme = theme;
        Updates = updates;
        AppUpdate = appUpdate;
        Mcp = mcp;
        McpServer = mcpServer;
        SettingsFilePath = store.FilePath;

        Paths = new ObservableCollection<PathSettingViewModel>
        {
            new(store, "plugin_path", "Папка плагинов",
                "DLL плагинов источников (ModuleBuilds). Меняется только после перезапуска.",
                paths.PluginPath, "ModuleBuilds", SettingPathKind.Folder, filePicker),
            new(store, "database_path", "Файл базы данных",
                "Embedded LiteDB. Перезапуск перецепит подключение и создаст бэкап нового файла.",
                paths.DatabasePath, "MyData.db", SettingPathKind.File, filePicker),
            new(store, "temp_path", "Папка временных файлов",
                "Кэш загрузок и трансформаций. Можно вынести на быстрый диск.",
                paths.TempPath, "temp", SettingPathKind.Folder, filePicker),
            new(store, "state_path", "Папка состояния источников",
                "Куки и сессии плагинов: YouTube/RuTube/VK/Telegram.",
                paths.StatePath, "state", SettingPathKind.Folder, filePicker),
        };

        Sections = new(
            new(SettingsSectionKeys.Paths, "Пути", PackIconLucideKind.FolderTree, "база плагины папка temp"),
            new(SettingsSectionKeys.Appearance, "Интерфейс", PackIconLucideKind.Palette, "тема шрифт масштаб заголовок стартовая страница запуск боковое меню навигация рейл свернуть"),
            new(SettingsSectionKeys.Notifications, "Уведомления", PackIconLucideKind.Bell, "тосты toast"),
            new(SettingsSectionKeys.Sync, "Синхронизация", PackIconLucideKind.RefreshCw, "синхронизация автосинк фон задержка старт"),
            new(SettingsSectionKeys.Updates, "Обновления", PackIconLucideKind.Download, "github релиз версия"),
            new(SettingsSectionKeys.Mcp, "Управление через ИИ (MCP)", PackIconLucideKind.Bot, "mcp ии управление автоматизация"));

        Reset = new(Paths, preferences, theme, updates, mcp, mcpOptions, OnFieldReset);

        Sections.Restore(store.GetStringValue(SelectedSectionKey));
        Sections.PropertyChanged += OnSectionsPropertyChanged;

        Mcp.PropertyChanged += OnMcpPropertyChanged;
        McpServer.PropertyChanged += OnMcpServerPropertyChanged;
        Preferences.PropertyChanged += OnHolderPropertyChanged;
        Theme.PropertyChanged += OnHolderPropertyChanged;
        Updates.PropertyChanged += OnHolderPropertyChanged;

        foreach (var path in Paths)
        {
            path.PropertyChanged += OnPathPropertyChanged;
            path.ErrorsChanged += OnPathErrorsChanged;
        }
    }

    public ObservableCollection<PathSettingViewModel> Paths { get; }

    public SettingsSectionList Sections { get; }

    public SettingsResetCatalog Reset { get; }

    public IReadOnlyList<StartupPageOption> StartupOptions => SettingsOptions.StartupPages;

    public StartupPageOption SelectedStartupOption
    {
        get => StartupOptions.FirstOrDefault(o => string.Equals(o.Key, Preferences.StartupSection, StringComparison.Ordinal))
            ?? StartupOptions[0];
        set
        {
            if (value is null || string.Equals(value.Key, Preferences.StartupSection, StringComparison.Ordinal))
            {
                return;
            }

            Preferences.StartupSection = value.Key;
            OnPropertyChanged();
        }
    }

    public ShellPreferences Preferences { get; }

    public ThemeViewModel Theme { get; }

    public UpdatePreferences Updates { get; }

    public AppUpdateViewModel AppUpdate { get; }

    public McpPreferences Mcp { get; }

    public McpServerHost McpServer { get; }

    public string SettingsFilePath { get; }

    public string McpUrl => $"http://127.0.0.1:{Mcp.Port}{_mcpOptions.EndpointPath}";

    public IReadOnlyList<SegmentOption> McpConnectFormats { get; } =
    [
        new(PackIconLucideKind.Braces, "JSON", "Фрагмент конфигурации MCP-клиента (mcpServers)"),
        new(PackIconLucideKind.Bot, "Claude Code", "Команда claude mcp add"),
        new(PackIconLucideKind.SquareTerminal, "Codex", "Блок для ~/.codex/config.toml"),
        new(PackIconLucideKind.SquareCode, "OpenCode", "Блок для opencode.json"),
        new(PackIconLucideKind.Link, "Адрес", "Адрес и заголовок для клиента, который спрашивает их формой"),
    ];

    public string McpConnectSnippet => McpConnectSnippets.For(SelectedMcpFormatIndex, McpUrl, Mcp.Token, _mcpOptions.ServerName);

    public bool McpElevatedWarning => Mcp.Enabled && McpServer.IsElevated;

    public string McpStatusText => McpServer.Endpoint is { } endpoint
        ? $"Сервер слушает {endpoint}"
        : Mcp.Enabled
            ? "Сервер включён, но не поднялся"
            : "Сервер выключен – подключиться к приложению снаружи не выйдет";

    public bool HasUnsavedChanges => Paths.Any(p => p.IsDirty);

    public bool HasInvalidPaths => Paths.Any(p => p.HasErrors);

    public bool RequiresRestart => HasUnsavedChanges;

    public bool HasSavedRecently => !string.IsNullOrEmpty(StatusMessage);

    public bool CanSave => HasUnsavedChanges && !HasInvalidPaths;

    [RelayCommand(CanExecute = nameof(HasUnsavedChanges))]
    private void RevertPaths()
    {
        foreach (var path in Paths)
        {
            path.Reset();
        }

        StatusMessage = null;
        RaiseDirtyFlags();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!Persist())
        {
            return;
        }

        StatusMessage = "Сохранено. Перезапустите приложение, чтобы новые пути применились.";
        RaiseDirtyFlags();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveAndRestart()
    {
        if (!_dialogs.ConfirmWarning(
                "Перезапуск приложения",
                "Приложение будет закрыто и запущено заново – несохранённая работа в других окнах процесса будет потеряна. Продолжить?"))
        {
            return;
        }

        if (!Persist())
        {
            return;
        }

        RaiseDirtyFlags();
        TryRestart();
    }

    [RelayCommand]
    private void OpenSettingsFile()
    {
        if (!File.Exists(SettingsFilePath))
        {
            File.WriteAllText(SettingsFilePath, string.Empty);
        }

        if (!_shellLauncher.Reveal(SettingsFilePath))
        {
            _logger.LogWarning("Не удалось открыть файл настроек в проводнике");
            _dialogs.Warning("Настройки", "Не удалось открыть проводник");
        }
    }

    [RelayCommand]
    private void CopySettingsPath()
    {
        CopyWithToast(
            SettingsFilePath,
            "Путь к файлу настроек скопирован в буфер обмена.",
            "Буфер обмена занят другим приложением – путь скопировать не удалось.");
    }

    [RelayCommand]
    private void CopyMcpToken()
    {
        CopyWithToast(
            Mcp.Token,
            "Токен MCP скопирован в буфер обмена.",
            "Буфер обмена занят другим приложением – токен скопировать не удалось.");
    }

    [RelayCommand]
    private void CopyMcpConnectSnippet()
    {
        CopyWithToast(
            McpConnectSnippet,
            "Строка подключения скопирована – вставьте её в настройки своего ИИ-помощника.",
            "Буфер обмена занят другим приложением – строку подключения скопировать не удалось.");
    }

    private void CopyWithToast(string text, string successMessage, string failureMessage)
    {
        if (_clipboard.TrySetText(text))
        {
            _toasts.Show(successMessage, StatusSeverity.Success);
            return;
        }

        _toasts.Show(failureMessage, StatusSeverity.Warning);
    }

    private void OnSectionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsSectionList.Selected) && Sections.Selected is { } section)
        {
            _store.SetValue(SelectedSectionKey, section.Key);
        }
    }

    private void OnHolderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellPreferences.StartupSection))
        {
            OnPropertyChanged(nameof(SelectedStartupOption));
        }

        RefreshResetState();
    }

    private void OnMcpPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(McpPreferences.Port) or nameof(McpPreferences.Token))
        {
            OnPropertyChanged(nameof(McpUrl));
            OnPropertyChanged(nameof(McpConnectSnippet));
        }

        if (e.PropertyName is nameof(McpPreferences.Enabled))
        {
            OnPropertyChanged(nameof(McpStatusText));
            OnPropertyChanged(nameof(McpElevatedWarning));
        }

        RefreshResetState();
    }

    private void OnMcpServerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(McpServerHost.Endpoint))
        {
            OnPropertyChanged(nameof(McpStatusText));
        }
    }

    private bool Persist()
    {
        foreach (var path in Paths.Where(p => p.IsDirty))
        {
            if (!path.TryCommit())
            {
                _dialogs.Error("Настройки", $"Не удалось сохранить поле «{path.Title}».");
                return false;
            }
        }

        return true;
    }

    private void TryRestart()
    {
        var exe = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exe))
        {
            _logger.LogError("Environment.ProcessPath пуст, перезапуск невозможен");
            _dialogs.Warning("Настройки",
                "Не удалось определить путь к exe для перезапуска. Закройте и запустите приложение вручную.");
            return;
        }

        if (!_shellLauncher.Start(exe))
        {
            _logger.LogError("Не удалось запустить новый процесс для перезапуска");
            _dialogs.Error("Настройки", "Не удалось перезапустить приложение");
            return;
        }

        _lifetime.Shutdown();
    }

    private void OnPathPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PathSettingViewModel.IsDirty)
            && e.PropertyName != nameof(PathSettingViewModel.EditValue))
        {
            return;
        }

        if (!string.IsNullOrEmpty(StatusMessage))
        {
            StatusMessage = null;
        }

        RaiseDirtyFlags();
        RefreshResetState();
    }

    private void OnPathErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasInvalidPaths));
        SaveCommand.NotifyCanExecuteChanged();
        SaveAndRestartCommand.NotifyCanExecuteChanged();
    }

    private void RaiseDirtyFlags()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(RequiresRestart));
        OnPropertyChanged(nameof(CanSave));
        RevertPathsCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        SaveAndRestartCommand.NotifyCanExecuteChanged();
    }
}
