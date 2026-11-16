using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Merging;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Tests.Doubles;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class MergeDuplicatesSubViewModelTests
{
    private TestDomainContext _ctx = null!;
    private MergeDuplicatesSubViewModel _vm = null!;

    [SetUp]
    public void SetUp()
    {
        var plugin = Substitute.For<ISourceType>();
        plugin.Name.Returns("Youtube");

        _ctx = new([plugin]);
        _vm = new(
            _ctx.Orcestrator,
            _ctx.MediaMergeService,
            _ctx.ActionHolder,
            _ctx.Dialogs,
            _ctx.Settings,
            NullLogger<MergeDuplicatesSubViewModel>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _ctx.Dispose();
    }

    [Test]
    public void EnsureScopeLoaded_ИRefreshScope_ЗагружаютАктивныеИсточникиИСохраняютСнятыеФлажки()
    {
        SeedSources(
            CreateSource("s1", "YouTube"),
            CreateSource("s2", "RuTube"),
            CreateSource("s3", "Отключённый", isDisable: true));

        _vm.EnsureScopeLoaded();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.Scope.Select(x => x.SourceId), Is.EqualTo(new[] { "s2", "s1" }));
            Assert.That(_vm.Scope.All(x => x.IsChecked), Is.True);
        }

        _vm.Scope.First(x => x.SourceId == "s1").IsChecked = false;
        SeedSources(CreateSource("s4", "Telegram"));

        _vm.RefreshScopeCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.Scope.First(x => x.SourceId == "s1").IsChecked, Is.False);
            Assert.That(_vm.Scope.First(x => x.SourceId == "s2").IsChecked, Is.True);
            Assert.That(_vm.Scope.First(x => x.SourceId == "s4").IsChecked, Is.True);
        }
    }

    [Test]
    public async Task FindCandidatesCommand_ПриЧистыхИКонфликтныхДублях_ОтмечаетЧистыеИИсключаетКонфликтныеИзАвтослияния()
    {
        SeedSources(CreateSource("s1", "YouTube"), CreateSource("s2", "RuTube"));
        SeedMedias(
            CreateMedia("clean-1", "Обзор камеры", ("s1", "yt-1")),
            CreateMedia("clean-2", "Обзор камеры!", ("s2", "rt-1")),
            CreateMedia("conf-1", "Конфликтный ролик", ("s1", "yt-2")),
            CreateMedia("conf-2", "Конфликтный ролик", ("s1", "yt-3")));

        _vm.EnsureScopeLoaded();
        await _vm.FindCandidatesCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.Groups, Has.Count.EqualTo(1));
            Assert.That(_vm.Groups[0].NormalizedKey, Is.EqualTo("обзор камеры"));
            Assert.That(_vm.Groups[0].IsMergeChecked, Is.True);
            Assert.That(_vm.CheckedGroups, Is.EqualTo(1));
            Assert.That(_vm.MediasToRemove, Is.EqualTo(1));
            Assert.That(_vm.StatusText, Does.Contain("конфликтных: 1"));
            Assert.That(_vm.StatusText, Does.Contain("Разбор конфликтов"));
            Assert.That(_vm.Journal.Any(j => j.Level == OperationJournalLevel.Warning && j.Text.Contains("конфликтом источника: 1")), Is.True);
        }
    }

    [Test]
    public async Task ApplyCommand_ПриПодтверждении_ОбъединяетЧистыеГруппыВLiteDBИПишетЖурнал()
    {
        SeedSources(CreateSource("s1", "YouTube"), CreateSource("s2", "RuTube"));
        SeedMedias(
            CreateMedia("m1", "Урок 1", ("s1", "yt-1")),
            CreateMedia("m2", "Урок 1", ("s2", "rt-1")));

        _ctx.Dialogs.ConfirmWarning(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        _vm.EnsureScopeLoaded();
        await _vm.FindCandidatesCommand.ExecuteAsync(null);
        await _vm.ApplyCommand.ExecuteAsync(null);

        var remaining = _ctx.Orcestrator.GetMedias();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.Groups[0].IsApplied, Is.True);
            Assert.That(_vm.Groups[0].IsApplyError, Is.False);
            Assert.That(_vm.Groups[0].ResultText, Is.EqualTo("Объединено"));
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(remaining[0].Sources.Select(s => s.SourceId), Is.EquivalentTo(new[] { "s1", "s2" }));
            Assert.That(_vm.Journal.Any(j => j.Level == OperationJournalLevel.Success), Is.True);
            Assert.That(_vm.StatusText, Is.EqualTo("Готово: объединено 1 из 1"));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ApplyCommand_ПриОшибкеСлияния_УчитываетStopOnError(bool stopOnError)
    {
        SeedSources(CreateSource("s1", "YouTube"), CreateSource("s2", "RuTube"));
        var clean1A = CreateMedia("c1a", "А – Первое", ("s1", "yt-1"));
        var clean1B = CreateMedia("c1b", "А – Первое", ("s2", "rt-1"));
        var clean2A = CreateMedia("c2a", "Б – Второе", ("s1", "yt-2"));
        var clean2B = CreateMedia("c2b", "Б – Второе", ("s2", "rt-2"));
        SeedMedias(clean1A, clean1B, clean2A, clean2B);

        _ctx.Dialogs.ConfirmWarning(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        _vm.EnsureScopeLoaded();
        await _vm.FindCandidatesCommand.ExecuteAsync(null);

        // Внедряем конфликтную ссылку в первую группу после поиска, чтобы MediaMergeService.Apply(..., acceptConflicts: false) упал
        _vm.Groups[0].Group.Medias[1].Sources[0].SourceId = "s1";
        _vm.StopOnError = stopOnError;

        await _vm.ApplyCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.Groups[0].IsApplied, Is.True);
            Assert.That(_vm.Groups[0].IsApplyError, Is.True);
            Assert.That(_vm.Groups[1].IsApplied, Is.EqualTo(!stopOnError));
            Assert.That(_vm.Groups[1].IsApplyError, Is.False);
            Assert.That(_ctx.Orcestrator.GetMedias(), Has.Count.EqualTo(stopOnError ? 4 : 3));
        }
    }

    [Test]
    public async Task CancelCommand_ВоВремяВыполнения_ПрерываетПакетДоСледующейГруппы()
    {
        SeedSources(CreateSource("s1", "YouTube"), CreateSource("s2", "RuTube"));
        SeedMedias(
            CreateMedia("a1", "А – Первое", ("s1", "yt-1")),
            CreateMedia("a2", "А – Первое", ("s2", "rt-1")),
            CreateMedia("b1", "Б – Второе", ("s1", "yt-2")),
            CreateMedia("b2", "Б – Второе", ("s2", "rt-2")));

        _ctx.Dialogs.ConfirmWarning(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        _vm.EnsureScopeLoaded();
        await _vm.FindCandidatesCommand.ExecuteAsync(null);

        _vm.Groups[0].PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MergeGroupRowViewModel.IsApplied) && _vm.Groups[0].IsApplied)
            {
                _vm.CancelCommand.Execute(null);
            }
        };

        await _vm.ApplyCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.Groups[0].IsApplied, Is.True);
            Assert.That(_vm.Groups[1].IsApplied, Is.False);
            Assert.That(_vm.StatusText, Is.EqualTo("Отменено пользователем"));
            Assert.That(_ctx.Orcestrator.GetMedias(), Has.Count.EqualTo(3));
        }
    }

    private void SeedSources(params Source[] sources)
    {
        var col = _ctx.Database.GetCollection<Source>("sources");

        foreach (var source in sources)
        {
            col.Insert(source);
        }
    }

    private void SeedMedias(params Media[] medias)
    {
        var col = _ctx.Database.GetCollection<Media>("medias");

        foreach (var media in medias)
        {
            col.Insert(media);
        }
    }

    private static Source CreateSource(string id, string name, bool isDisable = false) =>
        new()
        {
            Id = id,
            TypeId = "Youtube",
            IsDisable = isDisable,
            Settings = new() { ["_system_name"] = name },
        };

    private static Media CreateMedia(string id, string title, params (string SourceId, string ExternalId)[] links) =>
        new()
        {
            Id = id,
            Title = title,
            Sources = links
                .Select(l => new MediaSourceLink
                {
                    SourceId = l.SourceId,
                    ExternalId = l.ExternalId,
                    Status = MediaStatus.Ok,
                })
                .ToList(),
        };
}
