using KeepShell.Bootstrap;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Bootstrap.Gallery;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class GalleryOptionsTests
{
    private const string DefaultDirectory = @"C:\shots";

    [OneTimeSetUp]
    public void RegisterThemes()
    {
        AppThemes.Register();
    }

    [Test]
    public void Без_аргументов_берутся_все_страницы_все_диалоги_три_темы_и_покой()
    {
        var options = GalleryOptions.Parse([], DefaultDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Directory, Is.EqualTo(DefaultDirectory));
            Assert.That(options.Pages, Is.EqualTo(GalleryPages.AllKeys));
            Assert.That(options.Dialogs, Is.EqualTo(GalleryDialogs.All));
            Assert.That(options.State, Is.EqualTo(GalleryStates.Idle));
            Assert.That(options.Themes, Is.EqualTo(new[] { AppThemes.LightKey, AppThemes.DarkKey, AppThemes.TarkovKey }));
            Assert.That(options.Arguments.Unknown, Is.Empty);
        }
    }

    [Test]
    public void Диалоги_разбираются_без_повторов_а_none_отключает_их_съёмку()
    {
        var picked = GalleryOptions.Parse(["--dialogs", "add-source,docs,add-source"], DefaultDirectory);
        var off = GalleryOptions.Parse(["--dialogs", GalleryOptions.NoneValue], DefaultDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(picked.Dialogs, Is.EqualTo(new[] { GalleryDialogs.AddSource, GalleryDialogs.Docs }));
            Assert.That(picked.Arguments.Unknown, Is.Empty);
            Assert.That(off.Dialogs, Is.Empty);
            Assert.That(off.Arguments.Unknown, Is.Empty);
        }
    }

    [Test]
    public void Ключ_pages_none_отключает_съёмку_страниц_без_ошибок()
    {
        var options = GalleryOptions.Parse(["--pages", GalleryOptions.NoneValue, "--dialogs", "app-update"], DefaultDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Pages, Is.Empty);
            Assert.That(options.Dialogs, Is.EqualTo(new[] { GalleryDialogs.AppUpdate }));
            Assert.That(options.Arguments.Unknown, Is.Empty);
        }
    }

    [Test]
    public void Ключ_all_для_страниц_и_диалогов_берёт_весь_набор_без_unknown()
    {
        var options = GalleryOptions.Parse(["--pages", GalleryOptions.AllValue, "--dialogs", GalleryOptions.AllValue], DefaultDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Pages, Is.EqualTo(GalleryPages.AllKeys));
            Assert.That(options.Dialogs, Is.EqualTo(GalleryDialogs.All));
            Assert.That(options.Arguments.Unknown, Is.Empty);
        }
    }

    [TestCase("idle", GalleryStates.Idle)]
    [TestCase("EMPTY", GalleryStates.Empty)]
    [TestCase("Busy", GalleryStates.Busy)]
    [TestCase("ErRoR", GalleryStates.Error)]
    public void Состояние_разбирается_без_учёта_регистра(string raw, string expected)
    {
        var options = GalleryOptions.Parse(["--state", raw], DefaultDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.State, Is.EqualTo(expected));
            Assert.That(options.Arguments.Unknown, Is.Empty);
        }
    }

    [Test]
    public void Незнакомые_страница_тема_диалог_и_состояние_докладываются_а_невалидный_список_откатывается()
    {
        var partial = GalleryOptions.Parse(
            ["--pages", "media,тьма", "--themes", "dark,неон", "--dialogs", "docs,призрак", "--state", "сломано"],
            DefaultDirectory);

        var allInvalid = GalleryOptions.Parse(
            ["--pages", "тьма", "--themes", "неон", "--dialogs", "призрак"],
            DefaultDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(partial.Pages, Is.EqualTo(new[] { "media" }));
            Assert.That(partial.Themes, Is.EqualTo(new[] { AppThemes.DarkKey }));
            Assert.That(partial.Dialogs, Is.EqualTo(new[] { GalleryDialogs.Docs }));
            Assert.That(partial.State, Is.EqualTo(GalleryStates.Idle));
            Assert.That(partial.Arguments.Unknown, Is.EquivalentTo(new[] { "неон", "тьма", "призрак", "сломано" }));

            Assert.That(allInvalid.Pages, Is.EqualTo(GalleryPages.AllKeys));
            Assert.That(allInvalid.Themes, Is.EqualTo(GalleryOptions.DefaultThemes));
            Assert.That(allInvalid.Dialogs, Is.EqualTo(GalleryDialogs.All));
        }
    }

    [Test]
    public void Ключ_plugins_резолвится_в_полный_путь()
    {
        var options = GalleryOptions.Parse(["--plugins", "ModuleBuilds"], DefaultDirectory);

        Assert.That(options.PluginPath, Is.EqualTo(Path.GetFullPath("ModuleBuilds")));
    }

    [Test]
    public async Task Параллельные_экземпляры_GalleryFixture_получают_изолированные_корни_и_удаляются()
    {
        var options = GalleryOptions.Parse([], DefaultDirectory);
        GalleryFixture? first = null;
        GalleryFixture? second = null;

        try
        {
            var created = await Task.WhenAll(
                Task.Run(() => GalleryFixture.Create(options)),
                Task.Run(() => GalleryFixture.Create(options)));

            first = created[0];
            second = created[1];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(first.Root, Is.Not.EqualTo(second.Root));
                Assert.That(File.Exists(first.Paths.DatabasePath), Is.True);
                Assert.That(File.Exists(second.Paths.DatabasePath), Is.True);
                Assert.That(File.Exists(first.SampleDocPath), Is.True);
                Assert.That(File.Exists(second.SampleDocPath), Is.True);
            }
        }
        finally
        {
            first?.Dispose();
            second?.Dispose();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(first!.Root), Is.False);
            Assert.That(Directory.Exists(second!.Root), Is.False);
        }
    }
}
