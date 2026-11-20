using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Serilog;

namespace MediaOrcestrator.Runner.Wpf.Views;

public static class ReplyEditorTemplateWarmer
{
    private static bool _prewarmed;

    public static void Prewarm()
    {
        if (_prewarmed)
        {
            return;
        }

        _prewarmed = true;

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try
            {
                var dummy = new ReplyEditorViewModel(
                    sourceId: string.Empty,
                    externalMediaId: string.Empty,
                    parentExternalCommentId: null,
                    depth: 0,
                    hasAuthors: false,
                    initialText: null,
                    submitHandler: static _ => Task.CompletedTask,
                    cancelHandler: static _ => { });

                var presenter = new ContentPresenter
                {
                    Content = dummy,
                };

                presenter.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
                presenter.Arrange(new(presenter.DesiredSize));
                presenter.UpdateLayout();
            }
            catch (Exception ex)
            {
                _prewarmed = false;
                Log.Warning(ex, "Не удалось подготовить шаблон редактора ответа");
            }
        }));
    }
}
