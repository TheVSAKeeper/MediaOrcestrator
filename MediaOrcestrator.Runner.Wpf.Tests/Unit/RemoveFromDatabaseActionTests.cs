using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.MediaContextMenu;
using MediaOrcestrator.Runner.Wpf.MediaContextMenu.Actions;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using NSubstitute;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class RemoveFromDatabaseActionTests
{
    private TestDomainContext _ctx = null!;
    private IMediaActionUi _ui = null!;
    private RemoveFromDatabaseAction _action = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new();
        _ui = Substitute.For<IMediaActionUi>();
        _action = new();
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public void Build_ПриРазномВыборе_ФормируетОжидаемыеПункты()
    {
        var actionCtx = _ctx.ActionContextFactory.Create(_ui);
        var empty = _action.Build(new([], null), actionCtx).ToList();

        var m1 = new Media { Id = "m1", Title = "Ролик 1" };
        var m2 = new Media { Id = "m2", Title = "Ролик 2" };

        var single = _action.Build(new([m1], null), actionCtx).ToList();
        var batch = _action.Build(new([m1, m2], null), actionCtx).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_action.Order, Is.EqualTo(710));
            Assert.That(empty, Is.Empty);
            Assert.That(single, Has.Count.EqualTo(1));
            Assert.That(single[0].Text, Is.EqualTo("Удалить из базы оркестратора"));
            Assert.That(batch, Has.Count.EqualTo(1));
            Assert.That(batch[0].Text, Is.EqualTo("Удалить из базы оркестратора (2)"));
        }
    }

    [Test]
    public async Task Execute_ПриОтменеВДиалоге_НеУдаляетМедиаИзБазы()
    {
        var media = new Media { Id = "m1", Title = "Оставить" };
        _ctx.Database.GetCollection<Media>("medias").Insert(media);
        _ctx.Dialogs.ConfirmWarning("Удаление из базы", Arg.Any<string>()).Returns(false);

        var actionCtx = _ctx.ActionContextFactory.Create(_ui);
        var item = _action.Build(new([media], null), actionCtx).Single();

        await item.Execute!();

        Assert.That(_ctx.Orcestrator.GetMedias(), Has.Count.EqualTo(1));
        _ui.DidNotReceive().NotifyDataChanged();
    }

    [Test]
    public async Task Execute_ПриПодтверждении_УдаляетМедиаИзБазыИУведомляетUi()
    {
        var m1 = new Media { Id = "m1", Title = "Удалить 1" };
        var m2 = new Media { Id = "m2", Title = "Удалить 2" };
        _ctx.Database.GetCollection<Media>("medias").InsertBulk([m1, m2]);
        _ctx.Dialogs.ConfirmWarning("Удаление из базы", Arg.Any<string>()).Returns(true);

        var actionCtx = _ctx.ActionContextFactory.Create(_ui);
        var item = _action.Build(new([m1, m2], null), actionCtx).Single();

        await item.Execute!();

        Assert.That(_ctx.Orcestrator.GetMedias(), Is.Empty);
        _ui.Received(1).NotifyDataChanged();
    }
}
