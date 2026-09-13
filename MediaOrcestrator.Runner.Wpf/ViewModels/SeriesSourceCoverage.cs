namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed record SeriesSourceCoverage(string SourceTitle, int Present, int Total)
{
    public string Display => $"{SourceTitle} {Present}/{Total}";

    public bool IsComplete => Total > 0 && Present == Total;
}
