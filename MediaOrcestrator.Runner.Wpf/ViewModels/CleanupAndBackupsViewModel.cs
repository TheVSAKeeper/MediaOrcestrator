using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class CleanupAndBackupsViewModel : ObservableObject
{
    private readonly ILogger<CleanupAndBackupsViewModel> _logger;
    [ObservableProperty]
    private bool _hasLoaded;

    [ObservableProperty]
    private string? _loadError;

    public CleanupAndBackupsViewModel(
        MaintenanceViewModel files,
        DatabaseCleanupViewModel database,
        ILogger<CleanupAndBackupsViewModel> logger)
    {
        Files = files;
        Database = database;
        _logger = logger;
        Files.CleanupFinished += OnCleanupFinished;
    }

    public MaintenanceViewModel Files { get; }

    public DatabaseCleanupViewModel Database { get; }

    public Task RefreshAsync() => LoadCommand.ExecutionTask is { IsCompleted: false } loading
        ? loading
        : LoadCommand.ExecuteAsync(null);

    private async void OnCleanupFinished(object? sender, EventArgs e)
    {
        if (LoadCommand.ExecutionTask is { IsCompleted: false } loading)
        {
            await loading;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        LoadError = null;

        try
        {
            await Task.WhenAll(Files.LoadSizesAsync(), Database.LoadDatabaseAsync());
            HasLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить сведения об очистке и бэкапах");
            if (!HasLoaded)
            {
                LoadError = "Не удалось прочитать сведения о файлах, бэкапах и базе данных.";
            }
        }
    }
}
