using MediaOrcestrator.Domain.Merging;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
public sealed class MergeCandidateFinderTests
{
    [Test]
    public void Дубли_с_конфликтом_источника_всё_равно_попадают_в_кандидаты()
    {
        var media = new List<Media>
        {
            MakeMedia("5. Основы юниттестов", ("vk", "a")),
            MakeMedia("5. Основы юниттестов", ("rt", "b")),
            MakeMedia("5. Основы юниттестов", ("vk", "c")),
            MakeMedia("5. Основы юниттестов", ("yt", "d")),
        };

        var groups = MergeCandidateFinder.FindGroups(media);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(groups, Has.Count.EqualTo(1), "Группа с конфликтом источника не должна молча выпадать из кандидатов");
            Assert.That(groups[0].Medias, Has.Count.EqualTo(4));
            Assert.That(groups[0].HasSourceCollision, Is.True, "Финдер обязан подсветить, что внутри есть конфликт источника");
        }
    }

    [Test]
    public void Чистая_группа_без_конфликта_остаётся_кандидатом_без_флага_коллизии()
    {
        var media = new List<Media>
        {
            MakeMedia("Видео", ("yt", "a")),
            MakeMedia("Видео", ("rt", "b")),
        };

        var groups = MergeCandidateFinder.FindGroups(media);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].HasSourceCollision, Is.False);
        }
    }

    [Test]
    public void Одиночное_медиа_группу_не_образует()
    {
        var media = new List<Media>
        {
            MakeMedia("Видео", ("yt", "a")),
            MakeMedia("Другое видео", ("rt", "b")),
        };

        Assert.That(MergeCandidateFinder.FindGroups(media), Is.Empty);
    }

    private static Media MakeMedia(string title, params (string SourceId, string ExternalId)[] links)
    {
        var media = new Media
        {
            Id = "media-" + Guid.NewGuid().ToString("N"),
            Title = title,
            Description = "desc",
            Sources = [],
        };

        foreach (var (sourceId, externalId) in links)
        {
            media.Sources.Add(new()
            {
                MediaId = media.Id,
                SourceId = sourceId,
                ExternalId = externalId,
                Status = MediaStatus.Ok,
                Title = title,
                Description = "desc",
                SortNumber = 1,
            });
        }

        return media;
    }
}
