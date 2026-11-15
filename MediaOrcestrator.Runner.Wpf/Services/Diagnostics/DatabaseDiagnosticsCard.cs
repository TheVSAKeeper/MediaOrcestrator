using CommunityToolkit.Mvvm.Input;
using KeepShell.Diagnostics;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.Services.Diagnostics;

public sealed class DatabaseDiagnosticsCard : PollingDiagnosticsCardBase
{
    private readonly DatabaseMaintenanceService _maintenance;
    private readonly DatabaseBackupService _backups;
    private readonly IShellLauncher _shellLauncher;

    public DatabaseDiagnosticsCard(
        DatabaseMaintenanceService maintenance,
        DatabaseBackupService backups,
        IShellLauncher shellLauncher,
        ILogger<DatabaseDiagnosticsCard> logger)
        : base(logger)
    {
        _maintenance = maintenance;
        _backups = backups;
        _shellLauncher = shellLauncher;

        Command = new RelayCommand(() => _shellLauncher.Open(Path.GetDirectoryName(_maintenance.DatabasePath)!));
    }

    public override string Title => "База данных и бэкапы";

    public override ICommand? Command { get; }

    public override string? CommandCaption => "Открыть папку";

    protected override Task<(DiagnosticsCardState State, IReadOnlyList<DiagnosticsCardRow> Rows)> SampleAsync()
    {
        return Task.Run<(DiagnosticsCardState, IReadOnlyList<DiagnosticsCardRow>)>(() =>
        {
            var exists = File.Exists(_maintenance.DatabasePath);
            var dbSize = _maintenance.MeasureDatabaseFile();
            var medias = _maintenance.CountCollection("medias");
            var sources = _maintenance.CountCollection("sources");
            var relations = _maintenance.CountCollection("source_relations");
            var comments = _maintenance.CountCollection("media_comments");
            var backups = _backups.GetBackups();
            var pending = _backups.GetPendingRestore();

            var state = !exists
                ? DiagnosticsCardState.Error
                : pending is not null || (medias > 0 && backups.Count == 0)
                    ? DiagnosticsCardState.Warning
                    : DiagnosticsCardState.Ok;

            IReadOnlyList<DiagnosticsCardRow> rows =
            [
                new("Файл БД", exists ? FormatBytes(dbSize) : "не найден") { Hint = _maintenance.DatabasePath },
                new("Медиа / Комментарии", $"{medias} / {comments}"),
                new("Источники / Связи", $"{sources} / {relations}"),
                new("Бэкапы", backups.Count == 0 ? "нет" : $"{backups.Count} шт.") { Hint = _maintenance.BackupsPath },
                new("Ожидает восстановления", string.IsNullOrWhiteSpace(pending) ? "нет" : pending),
            ];

            return (state, rows);
        });
    }
}
