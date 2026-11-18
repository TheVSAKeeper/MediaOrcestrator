using CommunityToolkit.Mvvm.ComponentModel;
using KeepShell.Bootstrap;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using MediaOrcestrator.Runner.Wpf.Services;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MaintenanceHubViewModel : ObservableObject, IPageHeader
{
    private const string TabSettingKey = "wpf.maintenance.tab";

    private static readonly MaintenanceSection[] TabOrder =
        [MaintenanceSection.Tasks, MaintenanceSection.Tools, MaintenanceSection.Cleanup];

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
            new(PackIconLucideKind.History, "История задач", "Активные и завершённые фоновые задачи"),
            new(PackIconLucideKind.Wrench, "Инструменты", "Установка и обновление ffmpeg, yt-dlp и deno"),
            new(PackIconLucideKind.Database, "Очистка и бэкапы", "Очистка файлов, бэкапы и коллекции БД"),
        ];

        _suppressPersist = true;
        try
        {
            if (int.TryParse(_settings.GetStringValue(TabSettingKey), out var saved)
                && saved >= 0
                && saved < Tabs.Count)
            {
                SelectSection((MaintenanceSection)(saved + 1));
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
        1 => _tools,
        2 => _cleanup,
        _ => _tasks,
    };

    public void SelectSection(MaintenanceSection section)
    {
        SelectedIndex = Math.Max(0, Array.IndexOf(TabOrder, section));
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (_suppressPersist || value < 0 || value >= TabOrder.Length)
        {
            return;
        }

        _settings.SetValue(TabSettingKey, ((int)TabOrder[value] - 1).ToString());
    }
}
