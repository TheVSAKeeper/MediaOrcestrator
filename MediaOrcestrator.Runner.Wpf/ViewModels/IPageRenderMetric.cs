using System.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public interface IPageRenderMetric : INotifyPropertyChanged
{
    string? RenderDurationText { get; }

    IReadOnlyList<PageRenderStage>? RenderStages { get; }
}

public sealed record PageRenderStage(string Name, string Value);
