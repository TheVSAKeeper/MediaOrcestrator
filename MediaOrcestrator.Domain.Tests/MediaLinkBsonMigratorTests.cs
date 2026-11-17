using LiteDB;
using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
file sealed class MediaLinkBsonMigratorTests
{
    [Test]
    public void BackfillMissingFields_заполняет_отсутствующие_и_null_поля_связи_не_затирая_пустую_строку()
    {
        using var db = new LiteDatabase(":memory:");
        var collection = db.GetCollection("medias");
        collection.Insert(new BsonDocument
        {
            ["_id"] = "media-1",
            ["Title"] = "Parent title",
            ["Description"] = "Parent description",
            ["Sources"] = new BsonArray
            {
                new BsonDocument
                {
                    ["SourceId"] = "src-missing",
                    ["ExternalId"] = "ext-1",
                },
                new BsonDocument
                {
                    ["SourceId"] = "src-null",
                    ["ExternalId"] = "ext-2",
                    ["Title"] = BsonValue.Null,
                    ["Description"] = BsonValue.Null,
                },
                new BsonDocument
                {
                    ["SourceId"] = "src-explicit",
                    ["ExternalId"] = "ext-3",
                    ["Title"] = "Custom title",
                    ["Description"] = "",
                },
            },
        });

        var migrated = MediaLinkBsonMigrator.BackfillMissingFields(db, new object());

        var stored = collection.FindById("media-1");
        var links = stored["Sources"].AsArray.Select(x => x.AsDocument).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(migrated, Is.EqualTo(1));
            Assert.That(links[0]["Title"].AsString, Is.EqualTo("Parent title"));
            Assert.That(links[0]["Description"].AsString, Is.EqualTo("Parent description"));
            Assert.That(links[1]["Title"].AsString, Is.EqualTo("Parent title"));
            Assert.That(links[1]["Description"].AsString, Is.EqualTo("Parent description"));
            Assert.That(links[2]["Title"].AsString, Is.EqualTo("Custom title"));
            Assert.That(links[2]["Description"].AsString, Is.EqualTo(""));
        }
    }

    [Test]
    public void BackfillMissingFields_без_пропущенных_полей_не_считает_документ_изменённым()
    {
        using var db = new LiteDatabase(":memory:");
        var collection = db.GetCollection("medias");
        collection.Insert(new BsonDocument
        {
            ["_id"] = "media-2",
            ["Title"] = "Parent title",
            ["Description"] = "Parent description",
            ["Sources"] = new BsonArray
            {
                new BsonDocument
                {
                    ["SourceId"] = "src-1",
                    ["Title"] = "",
                    ["Description"] = "",
                },
            },
        });

        var migrated = MediaLinkBsonMigrator.BackfillMissingFields(db);

        var stored = collection.FindById("media-2");
        var link = stored["Sources"].AsArray[0].AsDocument;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(migrated, Is.EqualTo(0));
            Assert.That(link["Title"].AsString, Is.EqualTo(""));
            Assert.That(link["Description"].AsString, Is.EqualTo(""));
        }
    }

    [Test]
    public void BackfillMissingFields_при_отсутствующих_полях_родителя_записывает_пустые_строки()
    {
        using var db = new LiteDatabase(":memory:");
        var collection = db.GetCollection("medias");
        collection.Insert(new BsonDocument
        {
            ["_id"] = "media-3",
            ["Sources"] = new BsonArray
            {
                new BsonDocument
                {
                    ["SourceId"] = "src-1",
                },
            },
        });

        var migrated = MediaLinkBsonMigrator.BackfillMissingFields(db);

        var stored = collection.FindById("media-3");
        var link = stored["Sources"].AsArray[0].AsDocument;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(migrated, Is.EqualTo(1));
            Assert.That(link.ContainsKey("Title"), Is.True);
            Assert.That(link.ContainsKey("Description"), Is.True);
            Assert.That(link["Title"].AsString, Is.EqualTo(string.Empty));
            Assert.That(link["Description"].AsString, Is.EqualTo(string.Empty));
        }
    }

    [Test]
    public void Orcestrator_Init_дозаполняет_отсутствующие_bson_поля_связей_из_родителя()
    {
        using var db = new LiteDatabase(":memory:");
        db.GetCollection("medias").Insert(new BsonDocument
        {
            ["_id"] = "media-4",
            ["Title"] = "Legacy title",
            ["Description"] = "Legacy desc",
            ["Sources"] = new BsonArray
            {
                new BsonDocument
                {
                    ["SourceId"] = "src-legacy",
                    ["ExternalId"] = "ext-legacy",
                    ["Status"] = MediaStatus.Ok,
                },
            },
        });

        var pluginManager = new PluginManager([], null!, NullLogger<PluginManager>.Instance);
        var orcestrator = new Orcestrator(
            pluginManager,
            db,
            null!,
            null!,
            null!,
            NullLogger<Orcestrator>.Instance);

        orcestrator.Init();

        var media = db.GetCollection<Media>("medias").FindById("media-4");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(media.Sources, Has.Count.EqualTo(1));
            Assert.That(media.Sources[0].Title, Is.EqualTo("Legacy title"));
            Assert.That(media.Sources[0].Description, Is.EqualTo("Legacy desc"));
        }
    }
}
