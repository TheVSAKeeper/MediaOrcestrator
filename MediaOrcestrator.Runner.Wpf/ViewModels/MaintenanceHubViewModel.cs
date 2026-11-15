using CommunityToolkit.Mvvm.ComponentModel;
using KeepShell.Bootstrap;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Runner.Wpf.Services;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MaintenanceHubViewModel : ObservableObject, IPageHeader
{
    private const string TabSettingKey = "wpf.maintenance.tab";

    private readonly ToolsViewModel _tools;
    private readonly CleanupAndBackupsViewModel _cleanup;
    private readonly TasksViewModel _tasks;
    private readonly ISettingsStore _settings;
    private bool _suppressPersist;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current))]
    private int _selectedIndex;

    public MaintenanceHubViewModel(
        ToolsViewModel tools,
        CleanupAndBackupsViewModel cleanup,
        TasksViewModel tasks,
        ISettingsStore settings)
    {
        _tools = tools;
        _cleanup = cleanup;
        _tasks = tasks;
        _settings = settings;

        Tabs =
        [
            new(PackIconLucideKind.Wrench, "Инструменты", "Установка и обновление ffmpeg, yt-dlp и deno"),
            new(PackIconLucideKind.Database, "Очистка и бэкапы", "Очистка файлов, бэкапы и коллекции БД"),
            new(PackIconLucideKind.History, "История задач", "Активные и завершённые фоновые задачи"),
        ];

        _suppressPersist = true;
        try
        {
            if (int.TryParse(_settings.GetStringValue(TabSettingKey), out var saved)
                && saved >= 0
                && saved < Tabs.Count)
            {
                SelectedIndex = saved;
            }
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    public string PageTitle => "Обслуживание";

    public string? PageDescription => "Внешние инструменты, очистка диска и резервных копий LiteDB, история фоновых задач.";

    public IReadOnlyList<SegmentOption> Tabs { get; }

    public object Current => SelectedIndex switch
    {
        1 => _cleanup,
        2 => _tasks,
        _ => _tools,
    };

    public void SelectSection(MaintenanceSection section)
    {
        SelectedIndex = section switch
        {
            MaintenanceSection.Cleanup => 1,
            MaintenanceSection.Tasks => 2,
            _ => 0,
        };
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.SetValue(TabSettingKey, value.ToString());
    }
}
