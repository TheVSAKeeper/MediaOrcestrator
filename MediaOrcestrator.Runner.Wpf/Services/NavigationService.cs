using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MediaOrcestrator.Runner.Wpf.Services;

public sealed class NavigationService : INavigationService
{
    private readonly Lazy<ShellViewModel> _shell;
    private readonly Lazy<MediaViewModel> _media;
    private readonly Lazy<OperationsViewModel> _operations;
    private readonly Lazy<MaintenanceHubViewModel> _maintenance;
    private readonly Lazy<SourcesRelationsViewModel> _sources;
    private readonly IDialogService _dialogs;

    public NavigationService(IServiceProvider sp, IDialogService dialogs)
    {
        _shell = new(sp.GetRequiredService<ShellViewModel>);
        _media = new(sp.GetRequiredService<MediaViewModel>);
        _operations = new(sp.GetRequiredService<OperationsViewModel>);
        _maintenance = new(sp.GetRequiredService<MaintenanceHubViewModel>);
        _sources = new(sp.GetRequiredService<SourcesRelationsViewModel>);
        _dialogs = dialogs;
    }

    public void OpenInspector(Media media)
    {
        var grid = _media.Value.Grid;
        var row = grid.Rows.FirstOrDefault(r => string.Equals(r.MediaId, media.Id, StringComparison.Ordinal));

        if (row is null)
        {
            return;
        }

        grid.OpenInspectorCommand.Execute(row);
        _shell.Value.NavigateByKey("media");
    }

    public void OpenSourcesGraph(SourceSyncRelation? highlight = null)
    {
        var shell = _shell.Value;
        var section = shell.Sections.FirstOrDefault(s => s.Title == "Источники и связи");

        if (section is not null)
        {
            shell.Selected = section;
        }

        if (highlight is not null)
        {
            _dialogs.Info("Раздел в разработке",
                $"Подсветка связи в графе появится позже. Открыта связь «{highlight.From.TitleFull} → {highlight.To.TitleFull}».");
        }
    }

    public void OpenOperations(OperationsTab tab, IReadOnlyList<Media> selection)
    {
        _shell.Value.NavigateToOperations(tab);
        _operations.Value.ApplyExternalSelection(tab, selection);
    }

    public Task OpenSourceAuthenticationAsync(SourceOperationAuthenticationException failure)
    {
        var shell = _shell.Value;
        var origin = shell.Selected;
        var opening = _sources.Value.OpenAuthenticationAsync(failure, origin?.Title, () =>
        {
            if (origin is not null)
            {
                shell.Selected = origin;
            }
        });
        shell.NavigateByKey("sources");
        return opening;
    }

    public void OpenMaintenance(MaintenanceSection section = MaintenanceSection.Tools)
    {
        _shell.Value.NavigateByKey("maintenance");
        _maintenance.Value.SelectSection(section);
    }
}
