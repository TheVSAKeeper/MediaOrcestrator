using CommunityToolkit.Mvvm.Input;
using KeepShell.Diagnostics;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.Services.Diagnostics;

public sealed class SourcesPluginsDiagnosticsCard : PollingDiagnosticsCardBase
{
    private readonly PluginManager _plugins;
    private readonly Orcestrator _orcestrator;

    public SourcesPluginsDiagnosticsCard(
        PluginManager plugins,
        Orcestrator orcestrator,
        INavigationService navigation,
        ILogger<SourcesPluginsDiagnosticsCard> logger)
        : base(logger)
    {
        _plugins = plugins;
        _orcestrator = orcestrator;

        Command = new RelayCommand(() => navigation.OpenSourcesGraph());
    }

    public override string Title => "Источники и плагины";

    public override ICommand? Command { get; }

    public override string? CommandCaption => "Источники";

    protected override Task<(DiagnosticsCardState State, IReadOnlyList<DiagnosticsCardRow> Rows)> SampleAsync()
    {
        return Task.Run<(DiagnosticsCardState, IReadOnlyList<DiagnosticsCardRow>)>(() =>
        {
            var pluginCount = _plugins.MediaSources.Count;
            var sources = _orcestrator.GetSources();
            var disabledCount = sources.Count(s => s.IsDisable);
            var relationsCount = _orcestrator.GetRelations().Count;
            var lastSync = sources
                .Where(s => s.LastSyncedAt.HasValue)
                .Select(s => s.LastSyncedAt!.Value)
                .OrderByDescending(d => d)
                .FirstOrDefault();

            var pluginNames = pluginCount == 0
                ? "не загружены"
                : string.Join(", ", _plugins.MediaSources.Values.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal));

            var state = pluginCount == 0 || disabledCount > 0
                ? DiagnosticsCardState.Warning
                : DiagnosticsCardState.Ok;

            IReadOnlyList<DiagnosticsCardRow> rows =
            [
                new("Плагины", $"{pluginCount} шт.") { Hint = pluginNames },
                new("Источники", disabledCount > 0 ? $"{sources.Count} (откл. {disabledCount})" : $"{sources.Count}"),
                new("Связи синка", $"{relationsCount}"),
                new("Последний синк", lastSync == default ? "не выполнялся" : $"{lastSync.ToLocalTime():dd.MM HH:mm}"),
            ];

            return (state, rows);
        });
    }
}
