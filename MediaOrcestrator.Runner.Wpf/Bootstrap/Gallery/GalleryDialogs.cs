using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap.Gallery;

internal static class GalleryDialogs
{
    public const string AddSource = "add-source";
    public const string SourceSettings = "source-settings";
    public const string AddRelation = "add-relation";
    public const string AppUpdate = "app-update";
    public const string CommentsAppearance = "comments-appearance";
    public const string CommentsFetch = "comments-fetch";
    public const string TasksDebug = "tasks-debug";
    public const string Docs = "docs";
    public const string WinFormsImport = "winforms-import";

    public static IReadOnlyList<string> All { get; } =
    [
        AddSource,
        SourceSettings,
        AddRelation,
        AppUpdate,
        CommentsAppearance,
        CommentsFetch,
        TasksDebug,
        Docs,
        WinFormsImport,
    ];

    public static string Section(string key)
    {
        return key switch
        {
            AddSource or SourceSettings or AddRelation or Docs => "sources",
            AppUpdate or WinFormsImport => "settings",
            CommentsAppearance or CommentsFetch => "comments",
            TasksDebug => "tasks",
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Неизвестный диалог галереи."),
        };
    }

    public static void Open(string key, IServiceProvider services, GalleryFixture fixture, ModalHostViewModel modals)
    {
        switch (key)
        {
            case AddSource:
                OpenAddSource(services, modals);
                break;

            case SourceSettings:
                OpenSourceSettings(services, modals);
                break;

            case AddRelation:
                OpenAddRelation(services, modals);
                break;

            case AppUpdate:
                OpenAppUpdate(services, modals);
                break;

            case CommentsAppearance:
                _ = modals.ShowAsync(new CommentsAppearanceDialogViewModel(new(), "{name}, ", CommentsLayoutMode.Flat));
                break;

            case CommentsFetch:
                _ = modals.ShowAsync(new CommentsFetchOptionsDialogViewModel(new()));
                break;

            case TasksDebug:
                _ = modals.ShowAsync(new TasksDebugViewModel(services.GetRequiredService<ActionHolder>()));
                break;

            case Docs:
                OpenDocs(services, fixture, modals);
                break;

            case WinFormsImport:
                OpenWinFormsImport(services, modals);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, "Неизвестный диалог галереи.");
        }
    }

    private static void OpenWinFormsImport(IServiceProvider services, ModalHostViewModel modals)
    {
        var viewModel = services.GetRequiredService<WinFormsImportDialogViewModel>();
        viewModel.Configure(isFirstRunPrompt: false);
        _ = modals.ShowAsync(viewModel);
    }

    private static void OpenAddSource(IServiceProvider services, ModalHostViewModel modals)
    {
        var plugins = services.GetRequiredService<Orcestrator>().GetSourceTypes().Values;
        _ = modals.ShowAsync(new AddSourceViewModel(plugins));
    }

    private static void OpenSourceSettings(IServiceProvider services, ModalHostViewModel modals)
    {
        var orcestrator = services.GetRequiredService<Orcestrator>();
        var sources = orcestrator.GetSources().ToList();
        var plugins = orcestrator.GetSourceTypes().Values.ToList();

        var source = sources.FirstOrDefault(s =>
                string.Equals(s.TypeId, "Youtube", StringComparison.OrdinalIgnoreCase)
                && plugins.Any(p => string.Equals(p.Name, s.TypeId, StringComparison.OrdinalIgnoreCase)))
            ?? sources.FirstOrDefault(s => plugins.Any(p => string.Equals(p.Name, s.TypeId, StringComparison.OrdinalIgnoreCase)))
            ?? throw new InvalidOperationException("В фикстуре нет источника с загруженным плагином для редактирования.");

        var plugin = plugins.First(p => string.Equals(p.Name, source.TypeId, StringComparison.OrdinalIgnoreCase));
        var viewModel = services.GetRequiredService<SourceSettingsViewModel>();
        viewModel.InitializeEdit(source, plugin, sources);
        _ = modals.ShowAsync(viewModel);
    }

    private static void OpenAddRelation(IServiceProvider services, ModalHostViewModel modals)
    {
        var sources = services.GetRequiredService<Orcestrator>().GetSources();
        _ = modals.ShowAsync(new AddRelationViewModel(sources));
    }

    private static void OpenAppUpdate(IServiceProvider services, ModalHostViewModel modals)
    {
        var info = new AppUpdateInfo(
            "99.0.0",
            "https://github.com/TheVSAKeeper/MediaOrcestrator/releases/download/v99.0.0/MediaOrcestrator-Wpf-v99.0.0.zip",
            "## What's Changed\n- Расширенная галерея диалогов и состояний\n- Автоматические live smoke-тесты интерфейса WPF",
            42_500_000);

        var viewModel = new AppUpdateDialogViewModel(
            services.GetRequiredService<AppUpdateManager>(),
            info,
            services.GetRequiredService<ILogger<AppUpdateDialogViewModel>>(),
            services.GetRequiredService<IShellLauncher>());

        _ = modals.ShowAsync(viewModel);
    }

    private static void OpenDocs(IServiceProvider services, GalleryFixture fixture, ModalHostViewModel modals)
    {
        var viewModel = new DocumentationDialogViewModel(
            "Руководство источника YouTube",
            [fixture.SampleDocPath],
            services.GetRequiredService<IShellLauncher>(),
            fixture.SampleDocPath);

        _ = modals.ShowAsync(viewModel);
    }
}
