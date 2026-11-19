using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.MediaContextMenu;
using MediaOrcestrator.Runner.Wpf.Services;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class MediaGridViewModelTests
{
    private TestDomainContext _ctx = null!;
    private List<Source> _sources = null!;
    private IMediaActionUi _actionUi = null!;
    private MediaFilterViewModel _filter = null!;
    private MediaInspectorViewModel _inspector = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new();
        _sources = [];
        _actionUi = Substitute.For<IMediaActionUi>();
        _filter = new(_ctx.Settings, _ctx.Dispatcher);
        _inspector = new(
            NullLogger<MediaInspectorViewModel>.Instance,
            _ctx.Settings,
            _ctx.ShellLauncher,
            _ctx.Clipboard);
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public void ApplyDomainData_СМетаданнымиИГруппировкой_ЗаполняетСтрокиКолонкиИПокрытиеСерий()
    {
        var src1 = CreateSource("s1", "YouTube", "Youtube");
        var src2 = CreateSource("s2", "RuTube", "Rutube");
        _sources.AddRange([src1, src2]);

        var m1 = CreateMedia("m1", "Alpha – Серия 1", src1.Id, MediaStatus.Ok, ("Views", "120", "Просмотры"));
        var m2 = CreateMedia("m2", "Alpha – Серия 2", src2.Id, MediaStatus.Ok);

        _filter.ApplyDomainData(_sources, [m1, m2], []);
        _filter.MetadataColumnFilters.First().IsChecked = true;

        var grid = CreateGrid();
        grid.IsGrouped = true;
        grid.ApplyDomainData([m1, m2]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(grid.Rows, Has.Count.EqualTo(2));
            Assert.That(grid.ColumnSources.Select(s => s.Id), Is.EqualTo(new[] { "s1", "s2" }));
            Assert.That(grid.MetadataColumns, Has.Count.EqualTo(1));
            Assert.That(grid.MetadataColumns[0].Key, Is.EqualTo("Views"));
            Assert.That(grid.Rows[0].MetadataCells[0], Is.EqualTo("120"));
            Assert.That(grid.IsGrouped, Is.True);
            Assert.That(grid.RowsView.GroupDescriptions, Has.Count.EqualTo(1));
            Assert.That(grid.SeriesCoverage, Is.Not.Empty);
        }
    }

    [Test]
    public void ПоискВФильтре_СДебаунсом_ФильтруетСтрокиПослеСрабатыванияТаймера()
    {
        var src = CreateSource("s1", "YouTube", "Youtube");
        _sources.Add(src);

        var m1 = CreateMedia("m1", "Обзор камеры", src.Id, MediaStatus.Ok);
        var m2 = CreateMedia("m2", "Настройка звука", src.Id, MediaStatus.Ok);

        _filter.ApplyDomainData(_sources, [m1, m2], []);

        var grid = CreateGrid();
        grid.ApplyDomainData([m1, m2]);

        _filter.SearchText = "каме";
        var countBeforeTick = grid.VisibleCount;

        _filter.SearchText = "звука";
        _ctx.Dispatcher.Timers[0].FireTick();
        var countAfterTick = grid.VisibleCount;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(countBeforeTick, Is.EqualTo(2));
            Assert.That(countAfterTick, Is.EqualTo(1));
            Assert.That(grid.RowsView.Cast<MediaRowViewModel>().Single().Title, Is.EqualTo("Настройка звука"));
        }
    }

    [Test]
    public void OrderBySelection_ПриНепоследовательномВыборе_СохраняетПорядокКликовИИсключаетСнятые()
    {
        var src = CreateSource("s1", "YouTube", "Youtube");
        _sources.Add(src);

        var m0 = CreateMedia("m0", "Первое", src.Id, MediaStatus.Ok);
        var m1 = CreateMedia("m1", "Второе", src.Id, MediaStatus.Ok);
        var m2 = CreateMedia("m2", "Третье", src.Id, MediaStatus.Ok);

        _filter.ApplyDomainData(_sources, [m0, m1, m2], []);
        var grid = CreateGrid();
        grid.ApplyDomainData([m0, m1, m2]);

        grid.ApplySelectionChange([], [grid.Rows[2]]);
        grid.ApplySelectionChange([], [grid.Rows[0], grid.Rows[1]]);
        grid.ApplySelectionChange([grid.Rows[0]], []);

        _ctx.Dispatcher.FireAllRunningTimers();

        grid.OpenMergeOperationCommand.Execute(null);
        var orderDirect = grid.OrderBySelection([m0, m1, m2]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(grid.SelectedCount, Is.EqualTo(2));
            Assert.That(orderDirect.Select(m => m.Id), Is.EqualTo(new[] { "m2", "m1", "m0" }));
            _ctx.Navigation.Received(1).OpenOperations(
                OperationsTab.Merge,
                Arg.Is<IReadOnlyList<Media>>(list => list.Select(m => m.Id).SequenceEqual(new[] { "m2", "m1" })));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ФильтрИсточников_ПриПерестроенииСтрок_СохраняетВидимуюЧастьВыделенияВПорядкеВыбора(bool emptyIds)
    {
        var firstSource = CreateSource("s1", "YouTube", "Youtube");
        var secondSource = CreateSource("s2", "RuTube", "Rutube");
        _sources.AddRange([firstSource, secondSource]);
        var first = CreateMedia(emptyIds ? "" : "m1", "Первое", firstSource.Id, MediaStatus.Ok);
        var hidden = CreateMedia("m2", "Второе", secondSource.Id, MediaStatus.Ok);
        var last = CreateMedia(emptyIds ? "" : "m3", "Третье", firstSource.Id, MediaStatus.Ok);
        _filter.ApplyDomainData(_sources, [first, hidden, last], []);
        var grid = CreateGrid();
        grid.ApplyDomainData([first, hidden, last]);
        grid.ApplySelectionChange([], [grid.Rows[2], grid.Rows[1], grid.Rows[0]]);

        _filter.SourceFilters.Single(option => option.Id == firstSource.Id).IsChecked = true;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(grid.SelectedCount, Is.EqualTo(2));
            Assert.That(grid.SelectedRows, Is.EqualTo(new[] { grid.Rows[2], grid.Rows[0] }));
        }
    }

    [Test]
    public void КолонкиМетаданных_ПриОткрытойИзПередачСтроке_СохраняютВыделениеДоСменыФильтра()
    {
        var firstSource = CreateSource("s1", "YouTube", "Youtube");
        var secondSource = CreateSource("s2", "RuTube", "Rutube");
        _sources.AddRange([firstSource, secondSource]);
        var visible = CreateMedia("m1", "Архив", firstSource.Id, MediaStatus.Ok);
        var target = CreateMedia("m2", "Премьера", secondSource.Id, MediaStatus.Ok, ("Views", "120", "Просмотры"));
        _filter.ApplyDomainData(_sources, [visible, target], []);
        var grid = CreateGrid();
        grid.ApplyDomainData([visible, target]);
        _filter.SetSearchImmediateForAutomation(visible.Title);
        grid.OpenInspectorCommand.Execute(grid.Rows[1]);

        _filter.MetadataColumnFilters.Single().IsChecked = true;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(grid.RowsView.Cast<MediaRowViewModel>(), Does.Contain(grid.Rows[1]));
            Assert.That(grid.SelectedRows.Single(), Is.SameAs(grid.Rows[1]));
            Assert.That(grid.Focused?.Media, Is.SameAs(target));
        }

        _filter.SourceFilters.Single(option => option.Id == firstSource.Id).IsChecked = true;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(grid.RowsView.Cast<MediaRowViewModel>(), Does.Not.Contain(grid.Rows[1]));
            Assert.That(grid.SelectedCount, Is.Zero);
        }
    }

    [Test]
    public void Выделение_ПриПовторномДобавленииИСкрытойЦелиМеню_СохраняетТолькоВыбраннуюСтроку()
    {
        var source = CreateSource("s1", "YouTube", "Youtube");
        _sources.Add(source);
        var visible = CreateMedia("m1", "Архив", source.Id, MediaStatus.Ok);
        var hidden = CreateMedia("m2", "Премьера", source.Id, MediaStatus.Ok);
        _filter.ApplyDomainData(_sources, [visible, hidden], []);
        var grid = CreateGrid();
        grid.ApplyDomainData([visible, hidden]);
        _filter.SetSearchImmediateForAutomation(visible.Title);
        grid.ApplySelectionChange([], [grid.Rows[0]]);
        _ctx.Dispatcher.FireAllRunningTimers();

        grid.ApplySelectionChange([grid.Rows[1]], [grid.Rows[0], grid.Rows[0]]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(grid.SelectedCount, Is.EqualTo(1));
            Assert.That(grid.IsSelectionCurrent, Is.True);
        }

        grid.BuildContextMenu(grid.Rows[1], null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(grid.SelectedRows, Is.EqualTo(new[] { grid.Rows[0] }));
            Assert.That(grid.ContextMenuItems, Is.Empty);
        }
    }

    [Test]
    public void ДействиеПередачи_ПриСменеВыделенияДоТаймера_НедоступноДляПрежнейСтроки()
    {
        var source = CreateSource("s1", "YouTube", "Youtube");
        _sources.Add(source);
        var first = CreateMedia("m1", "Первое", source.Id, MediaStatus.Ok);
        var second = CreateMedia("m2", "Второе", source.Id, MediaStatus.Ok);
        var executions = 0;
        var builder = new MediaContextMenuBuilder(
            [new StubMenuAction(10, new MenuItemSpec("Передать")
            {
                IsTransferAction = true,
                Execute = () =>
                {
                    executions++;
                    return Task.CompletedTask;
                },
            })],
            NullLogger<MediaContextMenuBuilder>.Instance);
        _filter.ApplyDomainData(_sources, [first, second], []);
        var grid = CreateGrid(builder);
        grid.ApplyDomainData([first, second]);
        grid.ApplySelectionChange([], [grid.Rows[0]]);
        _ctx.Dispatcher.FireAllRunningTimers();
        var previous = grid.TransferMenuItems.Single(item => item.Text == "Передать").Command!;
        Assert.That(previous.CanExecute(null), Is.True);

        grid.ApplySelectionChange([grid.Rows[0]], [grid.Rows[1]]);
        previous.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(grid.SelectedCount, Is.EqualTo(1));
            Assert.That(previous.CanExecute(null), Is.False);
            Assert.That(executions, Is.Zero);
        }

        _ctx.Dispatcher.FireAllRunningTimers();
        var current = grid.TransferMenuItems.Single(item => item.Text == "Передать").Command!;
        Assert.That(current.CanExecute(null), Is.True);
        current.Execute(null);
        Assert.That(executions, Is.EqualTo(1));
    }

    [Test]
    public void ШиринаКолонок_ПриСохраненииОчисткеИПовторномСоздании_ОграничиваетсяДиапазономИПерсистится()
    {
        var grid = CreateGrid();

        grid.SetColumnWidth("source:s1", 10);
        var tooSmallIgnored = grid.TryGetColumnWidth("source:s1", out _);

        grid.SetColumnWidth("source:s1", 2500);
        var tooLargeIgnored = grid.TryGetColumnWidth("source:s1", out _);

        grid.SetColumnWidth("source:s1", double.NaN);
        var nanIgnored = grid.TryGetColumnWidth("source:s1", out _);

        grid.SetColumnWidth("source:s1", 20);
        grid.SetColumnWidth("source:s2", 2000);

        var reloadedGrid = CreateGrid();
        var hasS1AfterReload = reloadedGrid.TryGetColumnWidth("source:s1", out var s1Width);
        var hasS2AfterReload = reloadedGrid.TryGetColumnWidth("source:s2", out var s2Width);

        reloadedGrid.ClearColumnWidth("source:s1");
        var afterClearGrid = CreateGrid();
        var hasS1AfterClear = afterClearGrid.TryGetColumnWidth("source:s1", out _);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tooSmallIgnored, Is.False);
            Assert.That(tooLargeIgnored, Is.False);
            Assert.That(nanIgnored, Is.False);
            Assert.That(hasS1AfterReload, Is.True);
            Assert.That(s1Width, Is.EqualTo(20));
            Assert.That(hasS2AfterReload, Is.True);
            Assert.That(s2Width, Is.EqualTo(2000));
            Assert.That(hasS1AfterClear, Is.False);
        }
    }

    [Test]
    public void BuildTransferMenu_ПриСмешанныхДействияхМеню_ВключаетТолькоПунктыПередачи()
    {
        var src = CreateSource("s1", "YouTube", "Youtube");
        _sources.Add(src);
        var media = CreateMedia("m1", "Ролик", src.Id, MediaStatus.Ok);

        _filter.ApplyDomainData(_sources, [media], []);

        var builder = new MediaContextMenuBuilder(
            [
                new StubMenuAction(
                    order: 10,
                    new MenuItemSpec("Синхронизировать RuTube") { IsTransferAction = true },
                    new MenuItemSpec("Удалить с диска") { IsTransferAction = false }),
            ],
            NullLogger<MediaContextMenuBuilder>.Instance);

        var grid = CreateGrid(builder);
        grid.ApplyDomainData([media]);
        grid.ApplySelectionChange([], [grid.Rows[0]]);

        _ctx.Dispatcher.FireAllRunningTimers();

        var texts = grid.TransferMenuItems.Select(i => i.Text).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(grid.PanelActionItems.Any(i => i.Text == "Удалить с диска"), Is.True);
            Assert.That(texts, Does.Contain("RuTube"));
            Assert.That(texts, Does.Contain("Создать связь"));
            Assert.That(texts, Does.Not.Contain("Удалить с диска"));
            Assert.That(grid.TransferMenuItems.Count(i => i.IsSeparator), Is.EqualTo(1));
        }
    }

    private MediaGridViewModel CreateGrid(MediaContextMenuBuilder? menuBuilder = null) =>
        new(
            _filter,
            _inspector,
            _ctx.ActionContextFactory,
            _actionUi,
            menuBuilder ?? _ctx.ContextMenuBuilder,
            _ctx.Settings,
            () => _sources,
            _ctx.Dispatcher);

    private static Source CreateSource(string id, string name, string typeId) =>
        new()
        {
            Id = id,
            TypeId = typeId,
            Settings = new() { ["_system_name"] = name },
        };

    private static Media CreateMedia(
        string id,
        string title,
        string sourceId,
        string statusId,
        params (string Key, string Value, string DisplayName)[] metadata) =>
        new()
        {
            Id = id,
            Title = title,
            Sources =
            [
                new()
                {
                    SourceId = sourceId,
                    ExternalId = $"ext-{id}",
                    Status = statusId,
                },
            ],
            Metadata = metadata
                .Select(m => new MetadataItem
                {
                    SourceId = sourceId,
                    Key = m.Key,
                    Value = m.Value,
                    DisplayName = m.DisplayName,
                })
                .ToList(),
        };

    private sealed class StubMenuAction(int order, params MenuItemSpec[] specs) : IMediaMenuAction
    {
        public int Order { get; } = order;

        public IEnumerable<MenuItemSpec> Build(MediaSelection selection, MediaActionContext ctx) => specs;
    }
}
