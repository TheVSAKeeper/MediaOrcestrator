using KeepShell.Automation;
using KeepShell.Services.Platform;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using MediaOrcestrator.Runner.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap.Gallery;

internal sealed class GalleryHost : IGalleryHost
{
    public const string DialogKind = "dialog";

    private const string DialogFilePrefix = "dialog-";
    private const string StateTag = "state";
    private const int ModalAttempts = 40;
    private const int ModalPollMs = 25;

    private readonly IServiceProvider _services;
    private readonly GalleryFixture _fixture;
    private readonly GalleryOptions _options;
    private readonly ShellViewModel _shell;

    private GalleryHost(
        IServiceProvider services,
        GalleryFixture fixture,
        GalleryOptions options,
        IReadOnlyList<GalleryCase> cases)
    {
        _services = services;
        _fixture = fixture;
        _options = options;
        _shell = services.GetRequiredService<ShellViewModel>();
        Window = services.GetRequiredService<MainWindow>();
        Cases = cases;
        Tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [StateTag] = options.State,
        };
    }

    public string AppName => "Медиа оркестратор";

    public string AppVersion => App.ResolveAppVersion();

    public Window Window { get; }

    public IReadOnlyList<GalleryCase> Cases { get; }

    public IReadOnlyDictionary<string, string>? Tags { get; }

    public static GalleryHost Create(
        IServiceProvider services,
        GalleryFixture fixture,
        GalleryOptions options,
        ILogger logger)
    {
        var pages = options.Pages.Where(page => GalleryStates.SupportsPage(page, options.State)).ToList();
        var dialogs = options.Dialogs.Where(dialog => GalleryStates.SupportsDialog(dialog, options.State)).ToList();

        var dropped = options.Pages.Count - pages.Count + options.Dialogs.Count - dialogs.Count;

        if (dropped > 0)
        {
            logger.LogInformation("Галерея: в состоянии «{State}» пропущено кейсов: {Count}", options.State, dropped);
        }

        List<GalleryCase> cases =
        [
            .. pages.Select(page => new GalleryCase(GalleryCase.PageKind, page)),
            .. dialogs.Select(dialog => new GalleryCase(DialogKind, dialog)),
        ];

        return new(services, fixture, options, cases);
    }

    internal static async Task ArrangeAsync(IServiceProvider services)
    {
        services.GetRequiredService<ToolsViewModel>().SuppressAutoCheckForAutomation();
        await services.GetRequiredService<SourcesRelationsViewModel>().EnsureLoadedAsync().ConfigureAwait(true);
        await services.GetRequiredService<MediaViewModel>().EnsureLoadedAsync().ConfigureAwait(true);
        await services.GetRequiredService<AuditViewModel>().EnsureLoadedAsync().ConfigureAwait(true);
        await services.GetRequiredService<TransfersViewModel>().EnsureLoadedAsync().ConfigureAwait(true);
        await services.GetRequiredService<CommentsViewModel>().EnsureLoadedAsync().ConfigureAwait(true);
    }

    public Task ArrangeAsync() => ArrangeAsync(_services);

    public Task ThemeAppliedAsync(string themeKey)
    {
        _shell.IsTarkovBootPlaying = false;
        return Task.CompletedTask;
    }

    public async Task<GalleryShot> CaptureAsync(GalleryCase item, GalleryContext context)
    {
        _shell.Toasts.Toasts.Clear();

        var shot = item.Kind switch
        {
            DialogKind => await CaptureDialogAsync(item.Name, context).ConfigureAwait(true),
            _ => await CapturePageAsync(item.Name, context).ConfigureAwait(true),
        };

        return shot with
        {
            Tags = Tags,
        };
    }

    private async Task<GalleryShot> CapturePageAsync(string pageKey, GalleryContext context)
    {
        NavigateToPage(pageKey);

        try
        {
            await GalleryStates.ApplyPageAsync(pageKey, _options.State, _services).ConfigureAwait(true);
            await context.SettleAsync().ConfigureAwait(true);

            if (string.Equals(pageKey, "diagnostics", StringComparison.Ordinal))
            {
                await Task.Delay(250).ConfigureAwait(true);
                await context.SettleAsync().ConfigureAwait(true);
            }

            return context.Save(Slug(string.Empty, pageKey));
        }
        finally
        {
            GalleryStates.ResetPage(pageKey, _options.State, _services);
            await context.SettleAsync().ConfigureAwait(true);
        }
    }

    private async Task<GalleryShot> CaptureDialogAsync(string dialogKey, GalleryContext context)
    {
        var modals = _shell.Modal;
        var section = GalleryDialogs.Section(dialogKey);
        NavigateToPage(section);

        await context.SettleAsync().ConfigureAwait(true);
        GalleryDialogs.Open(dialogKey, _services, _fixture, modals);

        try
        {
            if (!await WaitForModalAsync(modals, true).ConfigureAwait(true))
            {
                throw new InvalidOperationException($"Диалог «{dialogKey}» не открылся за отведённое время.");
            }

            await context.SettleAsync().ConfigureAwait(true);

            return context.Save(Window, Slug(DialogFilePrefix, dialogKey));
        }
        finally
        {
            modals.RequestCancel();
            await WaitForModalAsync(modals, false).ConfigureAwait(true);
            await context.SettleAsync().ConfigureAwait(true);
        }
    }

    private void NavigateToPage(string pageKey)
    {
        var page = GalleryPages.Find(pageKey)
            ?? throw new InvalidOperationException($"Страница «{pageKey}» не описана в каталоге галереи.");

        if (!_shell.TryNavigate(page.Title))
        {
            throw new InvalidOperationException($"Страница «{page.Title}» не открылась – её нет в навигации.");
        }

        if (page.Section is { } section)
        {
            var hub = _services.GetRequiredService<MaintenanceHubViewModel>();
            hub.SelectSection(section switch
            {
                "tools" => MaintenanceSection.Tools,
                "cleanup" => MaintenanceSection.Cleanup,
                "tasks" => MaintenanceSection.Tasks,
                _ => throw new InvalidOperationException($"Неизвестная вкладка обслуживания «{section}»."),
            });
        }
    }

    private string Slug(string prefix, string name)
    {
        var state = GalleryStates.IsIdle(_options.State) ? string.Empty : $"-{_options.State}";
        return $"{prefix}{ViewCapture.Slug(name)}{state}";
    }

    private static async Task<bool> WaitForModalAsync(ModalHostViewModel modals, bool active)
    {
        for (var attempt = 0; attempt < ModalAttempts; attempt++)
        {
            if (modals.HasActive == active)
            {
                return true;
            }

            await Task.Delay(ModalPollMs).ConfigureAwait(true);
        }

        return modals.HasActive == active;
    }
}

internal sealed class GalleryJournal(ILogger logger) : IGalleryJournal
{
    public void Started(int cases, int themes, string directory)
    {
        logger.LogInformation("Галерея: {Cases} кейсов × {Themes} тем → {Directory}", cases, themes, directory);
    }

    public void CaseFailed(Exception exception, string caseName)
    {
        logger.LogError(exception, "Галерея: кадр «{Case}» пропущен", caseName);
    }

    public void Finished(int frames, int skipped, long elapsedMs)
    {
        logger.LogInformation("Галерея: снято {Frames}, пропущено {Skipped}, за {Elapsed} мс", frames, skipped, elapsedMs);
    }
}
