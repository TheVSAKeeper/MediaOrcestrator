using LiteDB;

namespace MediaOrcestrator.Domain;

public static class MediaLinkBsonMigrator
{
    public static int BackfillMissingFields(LiteDatabase db, object? writeLock = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (writeLock is not null)
        {
            lock (writeLock)
            {
                return BackfillMissingFieldsCore(db);
            }
        }

        return BackfillMissingFieldsCore(db);
    }

    private static int BackfillMissingFieldsCore(LiteDatabase db)
    {
        var collection = db.GetCollection("medias");
        var documents = collection.FindAll().ToList();
        var migrated = 0;

        foreach (var doc in documents)
        {
            if (!doc.TryGetValue("Sources", out var sourcesValue) || !sourcesValue.IsArray)
            {
                continue;
            }

            var parentTitle = GetStringOrEmpty(doc, "Title");
            var parentDescription = GetStringOrEmpty(doc, "Description");
            var dirty = false;

            foreach (var item in sourcesValue.AsArray)
            {
                if (item is not BsonDocument linkDoc)
                {
                    continue;
                }

                if (!linkDoc.ContainsKey("Title") || linkDoc["Title"].IsNull)
                {
                    linkDoc["Title"] = parentTitle;
                    dirty = true;
                }

                if (!linkDoc.ContainsKey("Description") || linkDoc["Description"].IsNull)
                {
                    linkDoc["Description"] = parentDescription;
                    dirty = true;
                }
            }

            if (!dirty)
            {
                continue;
            }

            collection.Update(doc);
            migrated++;
        }

        return migrated;
    }

    private static string GetStringOrEmpty(BsonDocument doc, string key)
    {
        if (!doc.TryGetValue(key, out var value) || value.IsNull)
        {
            return string.Empty;
        }

        return value.IsString ? value.AsString : value.RawValue?.ToString() ?? string.Empty;
    }
}
