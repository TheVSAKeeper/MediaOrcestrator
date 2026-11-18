using KeepShell.Bootstrap;
using KeepShell.Diagnostics;
using KeepShell.Services;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.Views;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Bootstrap.Gallery;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.Views;
using MediaOrcestrator.Runner.Wpf.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.Tests.Smoke;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed partial class LiveSmokeTests
{
    private static readonly string[] Themes = [AppThemes.LightKey, AppThemes.DarkKey, AppThemes.TarkovKey];

    private BindingErrorSink _sink = null!;
    private ServiceProvider _services = null!;
    private VisualTestHost _host = null!;
    private MainWindow _window = null!;
    private ShellViewModel _shell = null!;
    private ShellPreferences _preferences = null!;
    private GalleryFixture _fixture = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);

        var pluginPath = Path.Combine(ResolveRepoRoot(), "ModuleBuilds");
        var options = GalleryOptions.Parse(["--plugins", pluginPath], Path.Combine(Path.GetTempPath(), "MediaOrcestrator.Smoke"));
        _fixture = GalleryFixture.Create(options);

        ISettingsStore settings = new MemorySettings();
        settings.SetBool("wpf.mcp.enabled", false);

        AppThemes.Register();
        ThemeManager.Apply(AppThemes.LightKey);
        FontScaleManager.Initialize(FontScaleManager.DefaultScale);
        ViewLocator.InstallIntoApplication();

        var loggingOptions = new KeepShellLoggingOptions
        {
            LogsDirectory = Path.Combine(_fixture.Root, "logs"),
            FileNamePrefix = "wpf-",
        };

        _services = ServiceRegistration.BuildServiceProvider(_fixture.Paths, settings, new WpfLogSink(256), loggingOptions);
        _services.GetRequiredService<Orcestrator>().Init();

        _shell = _services.GetRequiredService<ShellViewModel>();
        _preferences = _services.GetRequiredService<ShellPreferences>();
        _window = _services.GetRequiredService<MainWindow>();

        _host = VisualTestHost.Show(_window);
        VisualTestHost.Run(() => GalleryHost.ArrangeAsync(_services));

        _sink = BindingErrorSink.Attach();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _sink?.Dispose();
        _host?.Dispose();
        _services?.Dispose();
        _fixture?.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        _sink.Clear();
        _shell.IsTarkovBootPlaying = false;
    }

    [Test]
    public void Потерянный_путь_биндинга_виден_тесту()
    {
        var probe = new TextBlock
        {
            DataContext = _shell,
        };

        probe.SetBinding(TextBlock.TextProperty, new Binding("НетТакогоСвойства"));
        Settle();

        Assert.That(_sink.Errors, Is.Not.Empty, "Сенсор ошибок биндинга молчит – остальные smoke-проверки невалидны.");
    }

    [Test]
    public void Страница_открывается_во_всех_темах_без_ошибок_привязки()
    {
        using var suppressed = ThemeTransitionOverlay.Suppress();

        try
        {
            foreach (var theme in Themes)
            {
                ApplyTheme(theme);

                foreach (var pageKey in GalleryPages.AllKeys)
                {
                    NavigateToPage(pageKey);
                    Settle();
                }
            }

            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
        finally
        {
            ApplyTheme(AppThemes.LightKey);
            Settle();
        }
    }

    [Test]
    public void Авторизация_ОткрываетСтрокиВоВсехТемахБезОшибокПривязки()
    {
        using var suppressed = ThemeTransitionOverlay.Suppress();
        var sources = _services.GetRequiredService<SourcesRelationsViewModel>();
        var previousMode = sources.ViewMode;

        try
        {
            NavigateToPage("sources");
            sources.IsAuthenticationMode = true;
            foreach (var theme in Themes)
            {
                ApplyTheme(theme);
                Settle();
                Assert.That(ViewCapture.Find(_window, "SessionList"), Is.TypeOf<ListBox>());
                var list = (ListBox)ViewCapture.Find(_window, "SessionList")!;
                Assert.That(list.Items, Is.Not.Empty);
                var firstRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.That(firstRow.Foreground, Is.SameAs(_window.FindResource("Fg.Primary")));
            }

            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
        finally
        {
            sources.ViewMode = previousMode;
            ApplyTheme(AppThemes.LightKey);
            Settle();
        }
    }

    [Test]
    public void Авторизация_ДиалогКодаИПароля_ОткрываетсяИОтменяется()
    {
        foreach (var password in new[] { false, true })
        {
            var prompt = new AuthenticationPromptViewModel("Введите данные для входа", "Продолжить", hasInput: true, password);
            var completion = _shell.Modal.ShowAsync(prompt);
            Settle();
            Assert.That(_shell.Modal.HasActive, Is.True);
            Assert.That(prompt.ConfirmCommand.CanExecute(null), Is.False);
            prompt.Value = "Тест";
            Assert.That(prompt.ConfirmCommand.CanExecute(null), Is.True);

            prompt.CancelCommand.Execute(null);
            Settle();
            Assert.That(completion.GetAwaiter().GetResult(), Is.False);
        }

        Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
    }

    [Test]
    public void Авторизация_СохранениеВБраузере_НеНазначаетсяКнопкойEnter()
    {
        var prompt = new AuthenticationPromptViewModel("Завершите вход в браузере.", "Проверить и сохранить",
            sourceTitle: "Учебный канал", accountHint: "Войдите в аккаунт Учебный канал.", waitsForBrowser: true);
        var completion = _shell.Modal.ShowAsync(prompt);
        try
        {
            Settle();
            var confirm = ViewCapture.Find(_window, "ConfirmButton") as Button;
            Assert.That(confirm, Is.Not.Null);
            Assert.That(confirm!.IsDefault, Is.False);
            Assert.That(confirm.IsEnabled, Is.False);
            prompt.IsReady = true;
            Settle();
            Assert.That(confirm.IsDefault, Is.False);
            Assert.That(confirm.IsEnabled, Is.True);
        }
        finally
        {
            prompt.CancelCommand.Execute(null);
            Settle();
        }

        Assert.That(completion.GetAwaiter().GetResult(), Is.False);
        Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
    }

    [Test]
    public void Диалог_открывается_во_всех_темах_без_ошибок_привязки()
    {
        using var suppressed = ThemeTransitionOverlay.Suppress();
        var modals = _shell.Modal;

        try
        {
            foreach (var theme in Themes)
            {
                ApplyTheme(theme);

                foreach (var dialogKey in GalleryDialogs.All)
                {
                    NavigateToPage(GalleryDialogs.Section(dialogKey));
                    Settle();

                    GalleryDialogs.Open(dialogKey, _services, _fixture, modals);

                    try
                    {
                        Settle();
                        Assert.That(modals.HasActive, Is.True, $"Диалог «{dialogKey}» не открылся в теме «{theme}».");
                    }
                    finally
                    {
                        modals.RequestCancel();
                        Settle();
                    }
                }
            }

            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
        finally
        {
            modals.RequestCancel();
            ApplyTheme(AppThemes.LightKey);
            Settle();
        }
    }

    [Test]
    public void Редактор_шаблона_обложки_открывается_во_всех_темах_без_ошибок_привязки()
    {
        using var suppressed = ThemeTransitionOverlay.Suppress();

        var viewModel = new CoverTemplateEditorViewModel(
            _services.GetRequiredService<CoverGenerator>(),
            _services.GetRequiredService<CoverTemplateStore>(),
            _services.GetRequiredService<IDialogService>(),
            _services.GetRequiredService<IFilePicker>(),
            _services.GetRequiredService<IUiDispatcher>(),
            null,
            "Тестовый профиль",
            "Хроники хранителя. Серия 01");

        var editorWindow = new CoverTemplateEditorWindow
        {
            DataContext = viewModel,
        };

        using var editorHost = VisualTestHost.Show(editorWindow, 1100, 760);

        try
        {
            foreach (var theme in Themes)
            {
                ApplyTheme(theme);
                editorHost.Settle();
            }

            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
        finally
        {
            ApplyTheme(AppThemes.LightKey);
            Settle();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Медиа_СтрелкаСортировкиНеПерекрываетУзкийЗаголовок(bool compact)
    {
        NavigateToPage("media");
        var vm = _services.GetRequiredService<MediaViewModel>().Grid;
        var originalCompact = vm.IsSourceHeaderCompact;
        var originalLength = vm.SourceHeaderCompactLength;
        vm.SourceHeaderCompactLength = 1;
        vm.IsSourceHeaderCompact = compact;
        Settle();

        var grid = Descendants<DataGrid>(_window).First(item => item.IsVisible && item.Name == "MediaGrid");
        var column = grid.Columns.First(item => item.Header is Source);
        var originalWidth = column.Width;
        var originalDirection = column.SortDirection;

        try
        {
            column.Width = new(column.MinWidth);
            column.SortDirection = ListSortDirection.Ascending;
            grid.ScrollIntoView(grid.Items[0], column);
            Settle();

            var header = Descendants<DataGridColumnHeader>(grid).Single(item => item.Column == column);
            var label = Descendants<ContentPresenter>(header).First();
            var arrow = Descendants<FrameworkElement>(header).Single(item => item.Name == "SortArrow");
            var labelBounds = label.TransformToAncestor(header).TransformBounds(new Rect(label.RenderSize));
            var arrowBounds = arrow.TransformToAncestor(header).TransformBounds(new Rect(arrow.RenderSize));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(label.ActualWidth, Is.GreaterThanOrEqualTo(20));
                Assert.That(arrow.IsVisible, Is.True);
                Assert.That(arrowBounds.Left, Is.GreaterThanOrEqualTo(labelBounds.Right));
            }
        }
        finally
        {
            column.Width = originalWidth;
            column.SortDirection = originalDirection;
            vm.SourceHeaderCompactLength = originalLength;
            vm.IsSourceHeaderCompact = originalCompact;
            Settle();
        }
    }

    [Test]
    public void Публикация_ПолеОписанияРастётОтДвухСтрокДоПрокрутки()
    {
        NavigateToPage("publish");
        Settle();
        var vm = _services.GetRequiredService<PublishViewModel>();
        var field = Descendants<TextBox>(_window).Single(item =>
            item.IsVisible && AutomationProperties.GetName(item) == "Описание");
        var originalDescription = vm.Description;

        try
        {
            vm.Description = string.Empty;
            Settle();
            var emptyHeight = field.ActualHeight;

            vm.Description = "Первый ряд\nВторой ряд";
            Settle();
            Assert.That(field.ActualHeight, Is.EqualTo(emptyHeight).Within(1.0));

            vm.Description = "Первый ряд\nВторой ряд\nТретий ряд";
            Settle();
            Assert.That(field.ActualHeight, Is.GreaterThan(emptyHeight));

            vm.Description = string.Join('\n', Enumerable.Repeat("Строка описания", 12));
            Settle();
            Assert.That(field.ExtentHeight, Is.GreaterThan(field.ViewportHeight));
        }
        finally
        {
            vm.Description = originalDescription;
            Settle();
        }
    }

    private void NavigateToPage(string pageKey)
    {
        var page = GalleryPages.Find(pageKey)
            ?? throw new InvalidOperationException($"Страница «{pageKey}» не описана в каталоге галереи.");

        Assert.That(_shell.TryNavigate(page.Title), Is.True, $"Страница «{page.Title}» не открылась.");

        if (page.Section is { } section)
        {
            var hub = _services.GetRequiredService<MaintenanceHubViewModel>();
            hub.SelectSection(section switch
            {
                "tools" => MaintenanceSection.Tools,
                "cleanup" => MaintenanceSection.Cleanup,
                "tasks" => MaintenanceSection.Tasks,
                _ => throw new InvalidOperationException($"Неизвестная секция обслуживания «{section}»."),
            });
        }
    }

    private void ApplyTheme(string theme)
    {
        ThemeManager.Apply(theme);
        _shell.IsTarkovBootPlaying = false;
        Settle();
    }

    private void Settle() => _host.Settle();

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MediaOrcestrator.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Не найден корень репозитория с MediaOrcestrator.sln.");
    }
}
