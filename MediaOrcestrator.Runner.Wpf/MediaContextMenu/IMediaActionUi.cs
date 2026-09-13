namespace MediaOrcestrator.Runner.Wpf.MediaContextMenu;

public interface IMediaActionUi
{
    void SetLoading(bool isLoading);

    void NotifyDataChanged();

    void ShowConvertProgress(double percent, string text);

    void HideConvertProgress();

    void RegisterConvertCancellation(CancellationTokenSource? cts);
}
