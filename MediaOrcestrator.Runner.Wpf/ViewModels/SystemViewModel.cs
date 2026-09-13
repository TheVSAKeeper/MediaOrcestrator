using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed class SystemSection(string title, ObservableObject content)
{
    public string Title { get; } = title;

    public ObservableObject Content { get; } = content;
}

public sealed partial class SystemViewModel : ObservableObject, IPageHeader
{
    public string PageTitle => "Система";

    public string? PageDescription => "Задачи, инструменты, логи и настройки приложения.";

    private readonly SystemSection _settingsSection;

    [ObservableProperty]
    private SystemSection? _selected;

    public SystemViewModel(
        TasksViewModel tasks,
        ToolsViewModel tools,
        LogsViewModel logs,
        SettingsViewModel settings,
        MaintenanceViewModel maintenance,
        DatabaseCleanupViewModel databaseCleanup,
        AboutViewModel about)
    {
        Tasks = tasks;
        Tools = tools;
        Logs = logs;
        Settings = settings;
        Maintenance = maintenance;
        DatabaseCleanup = databaseCleanup;
        About = about;
        _settingsSection = new("Настройки", settings);

        Sections =
        [
            new("Задачи", tasks),
            new("Инструменты", tools),
            new("Логи", logs),
            new("Диагностика", maintenance),
            new("Очистка БД", databaseCleanup),
            new("О программе", about),
            _settingsSection,
        ];

        Selected = Sections[0];
    }

    public ObservableCollection<SystemSection> Sections { get; }

    public TasksViewModel Tasks { get; }

    public ToolsViewModel Tools { get; }

    public LogsViewModel Logs { get; }

    public SettingsViewModel Settings { get; }

    public MaintenanceViewModel Maintenance { get; }

    public DatabaseCleanupViewModel DatabaseCleanup { get; }

    public AboutViewModel About { get; }

    public void SelectSettings()
    {
        Selected = _settingsSection;
    }
}
