using MediaOrcestrator.Domain;

namespace MediaOrcestrator.Runner.Wpf.Services;

public interface INavigationService
{
    void OpenInspector(Media media);

    void OpenOperations(OperationsTab tab, IReadOnlyList<Media> selection);

    void OpenSourcesGraph(SourceSyncRelation? highlight = null);

    void OpenMaintenance(MaintenanceSection section = MaintenanceSection.Tools);
}

public enum OperationsTab
{
    None = 0,
    Rename = 1,
    Preview = 2,
    Merge = 3,
    Conflicts = 4,
}

public enum MaintenanceSection
{
    None = 0,
    Tools = 1,
    Cleanup = 2,
    Tasks = 3,
}
