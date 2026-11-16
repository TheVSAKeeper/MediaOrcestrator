using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.Views.Modal;
using MediaOrcestrator.Runner.Wpf.Bootstrap.Gallery;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.Tests.Smoke;

public sealed partial class LiveSmokeTests
{
    private const double NarrowWindowWidth = 880;
    private const double NarrowWindowHeight = 584;
    private const double LargeFontScale = 1.6;

    private static readonly string[] StatesToCheck =
    [
        GalleryStates.Empty,
        GalleryStates.Busy,
        GalleryStates.Error,
    ];

    private static readonly string[] NarrowViewportPages =
    [
        "media",
        "transfers",
        "publish",
        "merge",
        "conflicts",
        "comments",
        "settings",
    ];

    private static readonly string[] NarrowViewportDialogs =
    [
        GalleryDialogs.AddSource,
        GalleryDialogs.AppUpdate,
        GalleryDialogs.CommentsFetch,
    ];

    [Test]
    public void Состояния_empty_busy_error_рендерятся_без_ошибок_привязки()
    {
        foreach (var state in StatesToCheck)
        {
            foreach (var pageKey in GalleryPages.AllKeys.Where(key => GalleryStates.SupportsPage(key, state)))
            {
                NavigateToPage(pageKey);

                try
                {
                    VisualTestHost.Run(() => GalleryStates.ApplyPageAsync(pageKey, state, _services));
                    Settle();
                }
                finally
                {
                    GalleryStates.ResetPage(pageKey, state, _services);
                    Settle();
                }
            }
        }

        Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
    }

    [Test]
    public void Узкое_окно_880x584_при_масштабе_шрифта_160_процентов_не_теряет_привязки_и_не_обрезает_управление()
    {
        var (oldWidth, oldHeight, oldScale) = (_window.Width, _window.Height, _preferences.FontScale);
        var modals = _shell.Modal;
        var primaryStyle = Application.Current.TryFindResource("Button.Primary") as Style;
        var problems = new List<string>();

        try
        {
            _window.SetCurrentValue(FrameworkElement.WidthProperty, NarrowWindowWidth);
            _window.SetCurrentValue(FrameworkElement.HeightProperty, NarrowWindowHeight);
            _preferences.FontScale = LargeFontScale;
            Settle();

            foreach (var pageKey in NarrowViewportPages)
            {
                NavigateToPage(pageKey);
                Settle();

                CollectViewportIssues($"Страница «{pageKey}»", _window, _window, primaryStyle, isModal: false, problems);
            }

            foreach (var dialogKey in NarrowViewportDialogs)
            {
                NavigateToPage(GalleryDialogs.Section(dialogKey));
                Settle();

                GalleryDialogs.Open(dialogKey, _services, _fixture, modals);

                try
                {
                    Settle();
                    Assert.That(modals.HasActive, Is.True, $"Диалог «{dialogKey}» не открылся в узком окне.");

                    var modalHost = Descendants<ModalHost>(_window).Single();
                    var surface = (FrameworkElement)(ViewCapture.Find(modalHost, "Surface")
                        ?? throw new InvalidOperationException("Не найдена поверхность ModalHost."));

                    var surfaceBounds = surface.TransformToAncestor(_window).TransformBounds(new(surface.RenderSize));

                    if (surfaceBounds.Left < -0.5 || surfaceBounds.Top < -0.5
                        || surfaceBounds.Right > _window.ActualWidth + 1 || surfaceBounds.Bottom > _window.ActualHeight + 1)
                    {
                        problems.Add(
                            $"Диалог «{dialogKey}»: окно диалога ({surfaceBounds.Left:F0},{surfaceBounds.Top:F0}–{surfaceBounds.Right:F0}x{surfaceBounds.Bottom:F0}) выходит за {_window.ActualWidth:F0}x{_window.ActualHeight:F0}");
                    }

                    CollectViewportIssues($"Диалог «{dialogKey}»", surface, _window, primaryStyle, isModal: true, problems);
                }
                finally
                {
                    modals.RequestCancel();
                    Settle();
                }
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            }
        }
        finally
        {
            modals.RequestCancel();
            _preferences.FontScale = oldScale;
            _window.SetCurrentValue(FrameworkElement.WidthProperty, oldWidth);
            _window.SetCurrentValue(FrameworkElement.HeightProperty, oldHeight);
            Settle();
        }
    }

    private static void CollectViewportIssues(
        string scope,
        DependencyObject root,
        Window window,
        Style? primaryStyle,
        bool isModal,
        List<string> problems)
    {
        var buttons = Descendants<Button>(root)
            .Where(button => button.IsVisible && (primaryStyle is null || ReferenceEquals(button.Style, primaryStyle)))
            .ToList();

        foreach (var button in buttons)
        {
            var bounds = button.TransformToAncestor(window).TransformBounds(new(button.RenderSize));
            var label = ResolveButtonLabel(button);

            if (button.ActualWidth <= 0 || button.ActualHeight <= 0)
            {
                problems.Add($"{scope}: кнопка «{label}» имеет нулевой размер ({button.ActualWidth:F0}x{button.ActualHeight:F0})");
                continue;
            }

            if (bounds.Left < -0.5 || bounds.Right > window.ActualWidth + 1)
            {
                problems.Add(
                    $"{scope}: кнопка «{label}» выходит по горизонтали ({bounds.Left:F0}..{bounds.Right:F0} при ширине {window.ActualWidth:F0})");
            }

            var insideScroller = !isModal && HasAncestor<ScrollViewer>(button, root);

            if (!insideScroller && (bounds.Top < -0.5 || bounds.Bottom > window.ActualHeight + 1))
            {
                problems.Add(
                    $"{scope}: кнопка «{label}» обрезана по вертикали ({bounds.Top:F0}..{bounds.Bottom:F0} при высоте {window.ActualHeight:F0})");
            }
        }
    }

    private static string ResolveButtonLabel(Button button)
    {
        if (button.Content is string text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var nestedText = Descendants<TextBlock>(button)
            .Select(t => t.Text)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

        return nestedText ?? AutomationProperties.GetName(button);
    }

    private static bool HasAncestor<T>(DependencyObject node, DependencyObject stopAt)
        where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(node);

        while (current is not null && !ReferenceEquals(current, stopAt))
        {
            if (current is T)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
