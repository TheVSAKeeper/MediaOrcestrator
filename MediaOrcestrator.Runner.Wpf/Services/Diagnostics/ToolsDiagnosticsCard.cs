using CommunityToolkit.Mvvm.Input;
using KeepShell.Diagnostics;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.Services.Diagnostics;

public sealed class ToolsDiagnosticsCard : PollingDiagnosticsCardBase
{
    private readonly ToolManager _tools;

    public ToolsDiagnosticsCard(
        ToolManager tools,
        INavigationService navigation,
        ILogger<ToolsDiagnosticsCard> logger)
        : base(logger)
    {
        _tools = tools;
        Command = new RelayCommand(() => navigation.OpenMaintenance(MaintenanceSection.Tools));
    }

    public override string Title => "Внешние инструменты";

    public override ICommand? Command { get; }

    public override string? CommandCaption => "Инструменты";

    protected override Task<(DiagnosticsCardState State, IReadOnlyList<DiagnosticsCardRow> Rows)> SampleAsync()
    {
        var registry = _tools.GetRegistry();

        if (registry.Count == 0)
        {
            IReadOnlyList<DiagnosticsCardRow> emptyRows =
            [
                new("Статус", "не требуются загруженными плагинами"),
            ];
            return Task.FromResult((DiagnosticsCardState.Unknown, emptyRows));
        }

        var rows = new List<DiagnosticsCardRow>(registry.Count);
        var missingCount = 0;

        foreach (var (name, descriptor) in registry.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var resolved = _tools.GetToolPath(name);

            if (string.IsNullOrWhiteSpace(resolved))
            {
                missingCount++;
                rows.Add(new(name, "не установлен") { Hint = $"Репозиторий: {descriptor.GitHubRepo}" });
                continue;
            }

            var companionsMissing = descriptor.CompanionExecutables?
                .Where(c => string.IsNullOrWhiteSpace(_tools.GetCompanionPath(name, c)))
                .ToArray() ?? [];

            if (companionsMissing.Length > 0)
            {
                missingCount++;
                rows.Add(new(name, $"нет: {string.Join(", ", companionsMissing)}") { Hint = resolved });
            }
            else
            {
                rows.Add(new(name, "готов") { Hint = resolved });
            }
        }

        var state = missingCount > 0 ? DiagnosticsCardState.Warning : DiagnosticsCardState.Ok;
        return Task.FromResult((state, (IReadOnlyList<DiagnosticsCardRow>)rows));
    }
}
