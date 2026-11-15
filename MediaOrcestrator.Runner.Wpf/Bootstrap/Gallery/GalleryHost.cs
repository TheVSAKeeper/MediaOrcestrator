using KeepShell.Automation;
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
    private readonly IServiceProvider _services;
    private readonly ShellViewModel _shell;
    private readonly Window _window;

    private GalleryHost(IServiceProvider services, IReadOnlyList<GalleryCase> cases)
    {
        _services = services;
        _shell = services.GetRequiredService<ShellViewModel>();
        _window = services.GetRequiredService<MainWindow>();
        Cases = cases;
    }

    public string AppName => "Медиа оркестратор";

    public string AppVersion => App.ResolveAppVersion();

    public Window Window => _window;

    public IReadOnlyList<GalleryCase> Cases { get; }

    public static GalleryHost Create(IServiceProvider services, GalleryOptions options)
    {
        return new(services, [.. options.Arguments.Pages.Select(page => new GalleryCase(GalleryCase.PageKind, page))]);
    }

    public async Task ArrangeAsync()
    {
        await _services.GetRequiredService<SourcesRelationsViewModel>().EnsureLoadedAsync();
        await _services.GetRequiredService<MediaViewModel>().EnsureLoadedAsync();
        await _services.GetRequiredService<AuditViewModel>().EnsureLoadedAsync();
        await _services.GetRequiredService<TransfersViewModel>().EnsureLoadedAsync();
        await _services.GetRequiredService<CommentsViewModel>().EnsureLoadedAsync();
    }

    public Task ThemeAppliedAsync(string themeKey)
    {
        _shell.IsTarkovBootPlaying = false;

        return Task.CompletedTask;
    }

    public async Task<GalleryShot> CaptureAsync(GalleryCase item, GalleryContext context)
    {
        var page = GalleryPages.Find(item.Name)
            ?? throw new InvalidOperationException($"Страница «{item.Name}» не описана в каталоге галереи.");

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

        await context.SettleAsync();

        if (string.Equals(page.Key, "diagnostics", StringComparison.Ordinal))
        {
            await Task.Delay(250);
            await context.SettleAsync();
        }

        return context.Save(page.Key);
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
