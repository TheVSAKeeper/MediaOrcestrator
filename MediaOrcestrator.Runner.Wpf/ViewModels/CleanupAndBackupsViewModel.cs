using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed class CleanupAndBackupsViewModel(
    MaintenanceViewModel files,
    DatabaseCleanupViewModel database) : ObservableObject
{
    public MaintenanceViewModel Files { get; } = files;

    public DatabaseCleanupViewModel Database { get; } = database;
}
