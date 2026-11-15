namespace MediaOrcestrator.Runner.MediaContextMenu;

public interface IMediaActionUi
{
    IWin32Window? Owner { get; }

    void SetLoading(bool isLoading);

    void NotifyDataChanged();

    void ShowConvertProgress(double percent, string text);

    void HideConvertProgress();

    void RegisterConvertCancellation(CancellationTokenSource? cts);
}
