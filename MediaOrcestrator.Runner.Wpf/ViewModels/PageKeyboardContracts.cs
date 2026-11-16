namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public interface IPageEscapeHandler
{
    bool CanHandleEscape { get; }

    bool TryHandleEscape();
}

public interface IPageSearchable
{
    event EventHandler? SearchFocusRequested;

    void RequestSearchFocus();
}
