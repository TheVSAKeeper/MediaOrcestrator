using CommunityToolkit.Mvvm.Input;
using KeepShell.Diagnostics;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using Microsoft.Extensions.Logging;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.Services.Diagnostics;

public sealed class StoragePathsDiagnosticsCard : PollingDiagnosticsCardBase
{
    private readonly DatabaseMaintenanceService _maintenance;
    private readonly ActionHolder _actionHolder;
    private readonly Paths _paths;

    public StoragePathsDiagnosticsCard(
        DatabaseMaintenanceService maintenance,
        ActionHolder actionHolder,
        Paths paths,
        INavigationService navigation,
        ILogger<StoragePathsDiagnosticsCard> logger)
        : base(logger)
    {
        _maintenance = maintenance;
        _actionHolder = actionHolder;
        _paths = paths;

        Command = new RelayCommand(() => navigation.OpenMaintenance(MaintenanceSection.Cleanup));
    }

    public override string Title => "Хранилище и задачи";

    public override ICommand? Command { get; }

    public override string? CommandCaption => "Очистка";

    protected override Task<(DiagnosticsCardState State, IReadOnlyList<DiagnosticsCardRow> Rows)> SampleAsync()
    {
        return Task.Run<(DiagnosticsCardState, IReadOnlyList<DiagnosticsCardRow>)>(() =>
        {
            var tempBytes = _maintenance.MeasureFolder(_paths.TempPath);
            var logsBytes = _maintenance.MeasureFolder(_maintenance.LogsPath);
            var backupsBytes = _maintenance.MeasureFolder(_maintenance.BackupsPath);
            var activeTasks = _actionHolder.ActiveCount;
            var recentErrors = _actionHolder.CompletedSnapshot().Count(a => a.State == ActionState.Failed);

            var state = recentErrors > 0 ? DiagnosticsCardState.Warning : DiagnosticsCardState.Ok;

            IReadOnlyList<DiagnosticsCardRow> rows =
            [
                new("Временные (temp)", FormatBytes(tempBytes)) { Hint = _paths.TempPath },
                new("Логи (logs)", FormatBytes(logsBytes)) { Hint = _maintenance.LogsPath },
                new("Бэкапы (backups)", FormatBytes(backupsBytes)) { Hint = _maintenance.BackupsPath },
                new("Состояния (state)", _paths.StatePath),
                new("Активные задачи / сбои", $"{activeTasks} / {recentErrors}"),
            ];

            return (state, rows);
        });
    }
}
