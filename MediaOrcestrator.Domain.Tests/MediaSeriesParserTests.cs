namespace MediaOrcestrator.Domain.Tests;

[TestFixture]
file sealed class MediaSeriesParserTests
{
    [Test]
    public void Разбирает_серию_и_номер_из_формата_со_слешем()
    {
        var info = MediaSeriesParser.Parse("GTA: Vice City Definitive Edition / #24 Сюжет. Автоцид");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.HasSeries, Is.True);
            Assert.That(info.RawSeriesName, Is.EqualTo("GTA: Vice City Definitive Edition"));
            Assert.That(info.EpisodeNumber, Is.EqualTo(24));
            Assert.That(info.EpisodeTitle, Is.EqualTo("#24 Сюжет. Автоцид"));
        }
    }

    [Test]
    public void Двоеточие_внутри_названия_не_считается_разделителем()
    {
        var info = MediaSeriesParser.Parse("Brand: Channel");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.HasSeries, Is.False);
            Assert.That(info.EpisodeNumber, Is.Null);
        }
    }

    [Test]
    public void Тире_и_вертикальная_черта_тоже_разделители()
    {
        var dash = MediaSeriesParser.Parse("Сериал — #3 Эпизод");
        var pipe = MediaSeriesParser.Parse("Шоу | #5 Часть");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dash.HasSeries, Is.True);
            Assert.That(dash.RawSeriesName, Is.EqualTo("Сериал"));
            Assert.That(dash.EpisodeNumber, Is.EqualTo(3));
            Assert.That(pipe.HasSeries, Is.True);
            Assert.That(pipe.RawSeriesName, Is.EqualTo("Шоу"));
            Assert.That(pipe.EpisodeNumber, Is.EqualTo(5));
        }
    }

    [Test]
    public void Без_разделителя_попадает_в_без_серии()
    {
        var info = MediaSeriesParser.Parse("Просто одиночное видео");

        Assert.That(info.HasSeries, Is.False);
    }

    [Test]
    public void Пустой_или_пробельный_title_без_серии()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(MediaSeriesParser.Parse(null).HasSeries, Is.False);
            Assert.That(MediaSeriesParser.Parse("   ").HasSeries, Is.False);
        }
    }

    [Test]
    public void Номер_эпизода_тянется_только_по_решётке()
    {
        var withHash = MediaSeriesParser.Parse("Серия / #2024 Финал");
        var withoutHash = MediaSeriesParser.Parse("Серия / Год 2024 без решётки");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withHash.EpisodeNumber, Is.EqualTo(2024));
            Assert.That(withoutHash.EpisodeNumber, Is.Null);
        }
    }

    [Test]
    public void Разные_регистр_и_пунктуация_сливаются_в_одну_серию()
    {
        var entries = MediaSeriesParser.Assign(
        [
            M("1", "Дум / #1 Старт"),
            M("2", "дум / #2 Продолжение"),
            M("3", "ДУМ! / #3 Финал"),
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries[0].SeriesKey, Is.EqualTo(entries[1].SeriesKey));
            Assert.That(entries[1].SeriesKey, Is.EqualTo(entries[2].SeriesKey));
            Assert.That(entries[0].SeriesName, Is.EqualTo(entries[2].SeriesName));
            Assert.That(entries[0].IsNoSeries, Is.False);
        }
    }

    [Test]
    public void Каноничное_имя_берёт_самое_частое()
    {
        var entries = MediaSeriesParser.Assign(
        [
            M("1", "Doom / #1"),
            M("2", "Doom / #2"),
            M("3", "DOOM / #3"),
        ]);

        Assert.That(entries.Select(e => e.SeriesName), Is.All.EqualTo("Doom"));
    }

    [Test]
    public void Медиа_без_серии_получают_метку_без_серии()
    {
        var entries = MediaSeriesParser.Assign([M("1", "одиночка")]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries[0].IsNoSeries, Is.True);
            Assert.That(entries[0].SeriesName, Is.EqualTo(MediaSeriesParser.NoSeriesName));
        }
    }

    private static Media M(string id, string title) => new() { Id = id, Title = title };
}
