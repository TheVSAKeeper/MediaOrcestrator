using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
public sealed class ChronologyPlannerTests
{
    private static readonly string[] ThreeChannels = ["ch1", "ch2", "ch3"];

    [Test]
    public void Сливает_видео_разных_источников_в_единый_хронологический_порядок()
    {
        var medias = new[]
        {
            MediaWith("m1", "ch1", "a", "2021-05-01T00:00:00+00:00"),
            MediaWith("m2", "ch2", "b", "2019-01-01T00:00:00+00:00"),
            MediaWith("m3", "ch3", "c", "2020-03-15T00:00:00+00:00"),
            MediaWith("m4", "ch1", "d", "2019-12-31T00:00:00+00:00"),
        };

        var plan = ChronologyPlanner.Plan(medias, ThreeChannels, Url);

        Assert.That(plan.Items.Select(i => i.ExternalId), Is.EqualTo(new[] { "b", "d", "c", "a" }));
    }

    [Test]
    public void Видео_без_даты_уходят_в_конец_и_попадают_в_MissingDateCount()
    {
        var medias = new[]
        {
            MediaWith("m1", "ch1", "withDate", "2020-01-01T00:00:00+00:00"),
            MediaWith("m2", "ch2", "noDate1", isoDate: null),
            MediaWith("m3", "ch3", "noDate2", isoDate: null),
        };

        var plan = ChronologyPlanner.Plan(medias, ThreeChannels, Url);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Items[0].ExternalId, Is.EqualTo("withDate"));
            Assert.That(plan.Items.Skip(1).Select(i => i.ExternalId), Is.EquivalentTo(new[] { "noDate1", "noDate2" }));
            Assert.That(plan.MissingDateCount, Is.EqualTo(2));
        }
    }

    [Test]
    public void Пропускает_медиа_не_привязанные_к_запрошенным_источникам()
    {
        var medias = new[]
        {
            MediaWith("m1", "ch1", "keep", "2020-01-01T00:00:00+00:00"),
            MediaWith("m2", "other", "drop", "2018-01-01T00:00:00+00:00"),
        };

        var plan = ChronologyPlanner.Plan(medias, ThreeChannels, Url);

        Assert.That(plan.Items.Select(i => i.ExternalId), Is.EqualTo(new[] { "keep" }));
    }

    [Test]
    public void Берёт_дату_того_источника_по_которому_строится_лента()
    {
        var media = MediaWith("m1", "ch1", "v", "2022-01-01T00:00:00+00:00");
        media.Metadata.Add(new() { Key = ChronologyPlanner.CreationDateKey, Value = "1999-01-01T00:00:00+00:00", SourceId = "other" });

        var plan = ChronologyPlanner.Plan([media], ThreeChannels, Url);

        Assert.That(plan.Items[0].PublishedAt, Is.EqualTo(DateTimeOffset.Parse("2022-01-01T00:00:00+00:00")));
    }

    [TestCase("2020-01-15T10:00:00.0000000+00:00", true)]
    [TestCase("2020-01-15T10:00:00Z", true)]
    [TestCase("", false)]
    [TestCase("не дата", false)]
    public void ExtractDate_парсит_ISO_и_отбраковывает_мусор(string value, bool expectParsed)
    {
        var metadata = new List<MetadataItem>
        {
            new() { Key = ChronologyPlanner.CreationDateKey, Value = value, SourceId = "ch1" },
        };

        var result = ChronologyPlanner.ExtractDate(metadata, "ch1");

        Assert.That(result.HasValue, Is.EqualTo(expectParsed));
    }

    private static string Url(string sourceId, string externalId, IReadOnlyList<MetadataItem>? metadata)
    {
        return $"https://yt/{externalId}";
    }

    private static Media MediaWith(string id, string sourceId, string externalId, string? isoDate, string title = "video")
    {
        var metadata = new List<MetadataItem>();
        if (isoDate is not null)
        {
            metadata.Add(new() { Key = ChronologyPlanner.CreationDateKey, Value = isoDate, SourceId = sourceId });
        }

        return new()
        {
            Id = id,
            Title = title,
            Metadata = metadata,
            Sources = [new MediaSourceLink { SourceId = sourceId, ExternalId = externalId, Title = title }],
        };
    }
}
