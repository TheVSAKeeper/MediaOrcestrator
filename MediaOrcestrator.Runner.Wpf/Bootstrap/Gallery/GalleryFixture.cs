using LiteDB;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Modules;
using System.Globalization;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap.Gallery;

internal sealed record GalleryFixture(string Root, Paths Paths, string SettingsPath)
{
    private const string FolderName = "MediaOrcestrator.Gallery";
    private const string HddSourceId = "hdd-archive";
    private const string YoutubeSourceId = "yt-keeper";
    private const string RutubeSourceId = "rt-mirror";
    private const string TelegramSourceId = "tg-digest";

    private static readonly DateTime Origin = new(2026, 2, 3, 19, 40, 0, DateTimeKind.Utc);

    private static readonly string[] Episodes =
    [
        "Разбор архива",
        "Куда девались бэкапы",
        "Три часа про кодеки",
        "Обложки, которые никто не увидит",
        "Стрим без сценария",
        "Ретроспектива за январь",
        "Один длинный монолог о том, зачем вообще нужен собственный оркестратор синхронизации медиа",
        "Гости из соседнего репозитория",
        "Чиним то, что не ломалось",
        "Ночная выгрузка",
        "Комментарии читаем вслух",
        "Планы на весну",
        "Хроника одного релиза",
        "Разговор про метаданные",
        "Уборка в базе",
        "Что осталось за кадром",
        "Итоги квартала",
        "Ответы на вопросы",
    ];

    public static GalleryFixture Create(GalleryOptions options)
    {
        var root = Path.Combine(Path.GetTempPath(), FolderName);

        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }

        Directory.CreateDirectory(root);

        var databasePath = Path.Combine(root, "MyData.db");

        using (var db = new LiteDatabase(databasePath))
        {
            Seed(db);
        }

        var paths = new Paths(
            options.PluginPath,
            databasePath,
            Path.Combine(root, "temp"),
            Path.Combine(root, "state"));

        return new(root, paths, Path.Combine(root, TomlSettingsFile.PrimaryFileName));
    }

    private static void Seed(LiteDatabase db)
    {
        var sources = db.GetCollection<Source>("sources");
        sources.Insert(NewSource(HddSourceId, "HardDiskDrive", "Архив на диске", Origin.AddHours(-2), new()
        {
            ["path"] = @"D:\Media\Хроники",
            ["dbFileName"] = "hdd.db",
        }));

        sources.Insert(NewSource(YoutubeSourceId, "Youtube", "YouTube: Хроники хранителя", Origin.AddHours(-5), new()
        {
            ["channel_id"] = "@keeper-chronicles",
            ["js_runtime"] = "deno",
        }));

        sources.Insert(NewSource(RutubeSourceId, "Rutube", "Rutube: зеркало", Origin.AddDays(-3), new()));
        sources.Insert(NewSource(TelegramSourceId, "Telegram", "Telegram: дайджест", null, new()));

        var relations = db.GetCollection<SourceSyncRelation>("source_relations");
        relations.Insert(new SourceSyncRelation { FromId = HddSourceId, ToId = YoutubeSourceId });
        relations.Insert(new SourceSyncRelation { FromId = HddSourceId, ToId = RutubeSourceId });
        relations.Insert(new SourceSyncRelation { FromId = YoutubeSourceId, ToId = TelegramSourceId, IsDisable = true });

        db.GetCollection<Media>("medias").InsertBulk(BuildMedias());
        db.GetCollection<CommentRecord>("media_comments").InsertBulk(BuildComments());
    }

    private static Source NewSource(string id, string typeId, string title, DateTime? lastSyncedAt, Dictionary<string, string> settings)
    {
        settings["_system_name"] = title;

        return new()
        {
            Id = id,
            TypeId = typeId,
            Settings = settings,
            LastSyncedAt = lastSyncedAt,
        };
    }

    private static List<Media> BuildMedias()
    {
        var medias = new List<Media>(Episodes.Length);

        for (var index = 0; index < Episodes.Length; index++)
        {
            var number = index + 1;
            var title = $"Хроники хранителя. Серия {number:00} – {Episodes[index]}";
            var created = Origin.AddDays(-Episodes.Length + index).AddMinutes(index * 7);
            var media = new Media
            {
                Id = $"media-{number:000}",
                Title = title,
                Description = $"Выпуск {number:00}: {Episodes[index]}. Запись из архива, перенесена в облачные источники по плану синхронизации.",
                Metadata =
                [
                    new()
                    {
                        Key = "CreationDate",
                        DisplayName = "Дата создания",
                        Value = created.ToString("O", CultureInfo.InvariantCulture),
                        DisplayType = "System.DateTime",
                    },
                    new()
                    {
                        Key = "Duration",
                        DisplayName = "Длительность",
                        Value = TimeSpan.FromMinutes(24 + (index * 3 % 47)).ToString(),
                        DisplayType = "System.TimeSpan",
                    },
                    new()
                    {
                        Key = "Size",
                        DisplayName = "Размер",
                        Value = (620_000_000L + (index * 137_000_000L)).ToString(CultureInfo.InvariantCulture),
                        DisplayType = "ByteSize",
                    },
                ],
            };

            media.Sources.Add(Link(media, HddSourceId, $"{number:000}", MediaStatus.Ok, number));

            media.Sources.Add(index switch
            {
                3 => Link(media, YoutubeSourceId, $"yt-{number:000}", MediaStatus.Error, number, "Квота API исчерпана, загрузка отложена"),
                7 => Link(media, YoutubeSourceId, $"yt-{number:000}", MediaStatus.Skipped, number),
                >= 14 => Link(media, YoutubeSourceId, string.Empty, MediaStatus.Missing, number),
                _ => Link(media, YoutubeSourceId, $"yt-{number:000}", MediaStatus.Ok, number),
            });

            media.Sources.Add(index switch
            {
                >= 9 => Link(media, RutubeSourceId, string.Empty, MediaStatus.Missing, number),
                5 => Link(media, RutubeSourceId, $"rt-{number:000}", MediaStatus.PartialOk, number, "Превью не загружено"),
                _ => Link(media, RutubeSourceId, $"rt-{number:000}", MediaStatus.Ok, number),
            });

            if (index < 4)
            {
                media.Sources.Add(Link(media, TelegramSourceId, $"tg-{number:000}", MediaStatus.Ok, number));
            }

            medias.Add(media);
        }

        return medias;
    }

    private static MediaSourceLink Link(Media media, string sourceId, string externalId, string status, int sortNumber, string? statusMessage = null)
    {
        return new()
        {
            SourceId = sourceId,
            ExternalId = externalId,
            Status = status,
            StatusMessage = statusMessage,
            SortNumber = sortNumber,
            MediaId = media.Id,
            Title = media.Title,
            Description = media.Description,
        };
    }

    private static List<CommentRecord> BuildComments()
    {
        (string Author, string Text, int Likes, bool IsAuthor)[] samples =
        [
            ("Пётр Кладовщиков", "Наконец-то выпуск про кодеки, ждал полгода.", 14, false),
            ("archive_lover", "А исходники обложек где-нибудь выложены?", 3, false),
            ("Хранитель", "Выложу в описании ближайшей серии.", 8, true),
            ("nightbuild", "На 12:40 звук пропадает секунд на пять.", 21, false),
            ("Хранитель", "Спасибо, перезалью дорожку.", 5, true),
            ("mediaman", "Часть про метаданные лучше бы отдельной серией.", 2, false),
            ("Аноним", "Первый!", 0, false),
            ("dev_ru", "Что за шрифт в оверлее?", 6, false),
            ("Хранитель", "Bahnschrift Condensed, тема из каркаса.", 11, true),
        ];

        var records = new List<CommentRecord>(samples.Length);

        for (var index = 0; index < samples.Length; index++)
        {
            var (author, text, likes, isAuthor) = samples[index];
            var externalMediaId = $"yt-{(index / 3) + 1:000}";
            var commentId = $"c-{index + 1:000}";

            records.Add(new()
            {
                Id = $"{YoutubeSourceId}|{externalMediaId}|{commentId}",
                SourceId = YoutubeSourceId,
                ExternalMediaId = externalMediaId,
                ExternalCommentId = commentId,
                ParentExternalCommentId = isAuthor ? $"c-{index:000}" : null,
                AuthorName = author,
                Text = text,
                PublishedAt = Origin.AddHours(-index * 9),
                LikeCount = likes,
                IsAuthor = isAuthor,
                CanEdit = isAuthor,
                CanDelete = isAuthor,
            });
        }

        return records;
    }
}
