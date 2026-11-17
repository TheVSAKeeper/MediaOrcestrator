using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
public sealed class SyncPlannerDanglingRelationTests
{
    private SyncPlanner _planner = null!;

    [SetUp]
    public void SetUp()
    {
        _planner = new(NullLogger<SyncPlanner>.Instance);
    }

    [Test]
    public void Plan_СвязьБезИсточникаОтправителя_ПропускаетсяБезОшибки()
    {
        var to = CreateSource("s2", "RuTube");
        var media = CreateMediaWithOk("m1", "ghost");
        var relations = new List<SourceSyncRelation>
        {
            new() { FromId = "ghost", ToId = to.Id, From = null!, To = to },
        };

        var intents = _planner.Plan([media], relations);

        Assert.That(intents, Is.Empty);
    }

    [Test]
    public void Plan_СвязьБезЦелевогоИсточникаИСоСтатусомOk_ПропускаетсяБезОшибки()
    {
        var from = CreateSource("s1", "YouTube");
        var media = CreateMediaWithOk("m1", from.Id);
        var relations = new List<SourceSyncRelation>
        {
            new() { FromId = from.Id, ToId = "ghost", From = from, To = null! },
        };

        var intents = _planner.Plan([media], relations);

        Assert.That(intents, Is.Empty);
    }

    [Test]
    public void Plan_ЦепочкаСБитымВторымШагом_СтроитТолькоЖивойКореньБезБитогоПотомка()
    {
        var s1 = CreateSource("s1", "YouTube");
        var s2 = CreateSource("s2", "RuTube");
        var media = CreateMediaWithOk("m1", s1.Id);
        var relations = new List<SourceSyncRelation>
        {
            new() { FromId = s1.Id, ToId = s2.Id, From = s1, To = s2 },
            new() { FromId = s2.Id, ToId = "ghost", From = s2, To = null! },
        };

        var intents = _planner.Plan([media], relations);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(intents, Has.Count.EqualTo(1));
            Assert.That(intents[0].From.Id, Is.EqualTo(s1.Id));
            Assert.That(intents[0].To.Id, Is.EqualTo(s2.Id));
            Assert.That(intents[0].NextIntents, Is.Empty);
        }
    }

    private static Source CreateSource(string id, string typeId) =>
        new()
        {
            Id = id,
            TypeId = typeId,
            Settings = new() { ["_system_name"] = id },
        };

    private static Media CreateMediaWithOk(string mediaId, string sourceId) =>
        new()
        {
            Id = mediaId,
            Title = "Тестовое видео",
            Sources =
            [
                new() { SourceId = sourceId, ExternalId = "ext-1", Status = MediaStatus.Ok },
            ],
        };
}
